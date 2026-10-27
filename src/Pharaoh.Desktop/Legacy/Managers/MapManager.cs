using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Environment;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Simulation;
using Pharaoh.Engine.Walkers;
using Pharaoh.Engine.UI;
using Pharaoh.Desktop.Legacy.Rendering;
using Pharaoh.Desktop.Legacy.UI;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>Owns city-map composition and the map/minimap render buffers.</summary>
internal sealed partial class MapManager(
    Sg3AssetCatalog graphicsAssets,
    BuildModeManager buildMode) : IDisposable
{
    private readonly Dictionary<int, uint> stableTerrainImages = [];
    private readonly Dictionary<int, uint> dynamicImageOverrides = [];
    private readonly Dictionary<int, Bitmap> mapSprites = [];
    private readonly Dictionary<int, Bitmap> demolitionSprites = [];
    private readonly Dictionary<int, Bitmap> isometricBaseSprites = [];
    private readonly Dictionary<int, Bitmap> isometricOverlaySprites = [];
    private readonly Dictionary<int, Bitmap> demolitionBaseSprites = [];
    private readonly Dictionary<int, Bitmap> demolitionOverlaySprites = [];
    private readonly Dictionary<int, RgbaImage> minimapSprites = [];
    private readonly Dictionary<int, int> animatedWaterImages = [];
    private readonly WalkerRenderer walkerRenderer = new(graphicsAssets);

    [OriginalAddress(0x004FA780,"FUN_004fa780")]
    public void DrawWalkerInspection(Graphics graphics,Walker walker,Rectangle bounds)=>walkerRenderer.DrawInspection(graphics,walker,bounds);
    private readonly BuildPreviewRenderer buildPreviewRenderer = new(graphicsAssets, buildMode);
    private LegacyGridBuilder? currentBuilder;
    private LegacyMissionSession? currentSession;
    private LegacyCityMapSnapshot? currentMap;
    private ReadOnlyMemory<byte> currentTerrainGrid;
    private LegacyWaterAnimation? waterAnimation;
    private int currentFrameWidth;
    private int currentFrameHeight;
    private bool currentCompactSidebar;
    private bool currentFullMap;
    private int stableTerrainOrientation = -1;
    private LegacyBuildingAnimationClock buildingAnimationClock=new();
    private readonly Dictionary<int,long> buildingVisualSignatures=[];
    private long buildingAnimationMilliseconds;
    private LegacyImageDrawCommand[] visibleCommands = [];
    private int[] commandPainterRanks = [];
    private int[] tilePainterRanks = new int[LegacyCityMapSnapshot.TileCount];
    private int lastPainterRank = -1;

    public void DrawHeldBuildingFootprint(Graphics graphics,CameraManager camera,IEnumerable<int> tiles)=>
        buildPreviewRenderer.DrawHeldFootprint(graphics,camera,tiles);

    public Bitmap? Frame { get; private set; }
    public Bitmap? MinimapFrame { get; private set; }
    public LegacyCityMinimapProjection? MinimapProjection { get; private set; }

    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    public void InvalidateTerrainTiles(IEnumerable<int> tiles)
    {
        ReadOnlySpan<byte> terrain = currentSession?.LogicalChunks.TryGetValue(
            "terrain_grid", out ReadOnlyMemory<byte> memory) == true
            ? memory.Span
            : ReadOnlySpan<byte>.Empty;
        foreach (int tile in tiles)
        {
            stableTerrainImages.Remove(tile);
            dynamicImageOverrides.Remove(tile);
            animatedWaterImages.Remove(tile);
            int x = tile % LegacyCityMapSnapshot.MapLength;
            int y = tile / LegacyCityMapSnapshot.MapLength;
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx >= LegacyCityMapSnapshot.MapLength ||
                    (uint)ny >= LegacyCityMapSnapshot.MapLength)
                    continue;
                int neighbor = ny * LegacyCityMapSnapshot.MapLength + nx;
                if (terrain.Length < (neighbor + 1) * sizeof(uint))
                    continue;
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                    terrain[(neighbor * sizeof(uint))..]);
                if ((flags & 0x00010004) == 0)
                    continue;
                stableTerrainImages.Remove(neighbor);
                animatedWaterImages.Remove(neighbor);
            }
        }
    }

    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    public void InvalidateFloodTerrainTiles(IEnumerable<int> tiles, int radius)
    {
        if (currentBuilder is null)
            return;
        foreach (int tile in tiles)
        foreach (int affected in FloodManager.EnumerateTerrainRefreshTiles(tile, radius))
        {
            // The original flood refresh operates on the river/floodplain
            // surface. Neighbouring desert cells remain on the persistent
            // background surface and must keep their selected artwork.
            if (!currentBuilder.IsFloodSurfaceTile(affected))
                continue;
            stableTerrainImages.Remove(affected);
            animatedWaterImages.Remove(affected);
        }
    }
    [OriginalAddress(0x00474130, "FUN_00474130")]
    public void DrawHousingPreview(Graphics graphics, CameraManager camera) =>
        buildPreviewRenderer.DrawHousing(graphics, camera);

    [OriginalAddress(0x00430EF0, "terrain/base city-map pass")]
    [OriginalAddress(0x00436B60, "row-ordered buildings, walkers and foreground pass")]
    [OriginalAddress(0x00438E50, "draw one walker")]
    public void DrawMapAndWalkers(Graphics graphics, CameraManager camera, Walkers walkers)
    {
        if (DrawRoadTerrainAndWalkers(graphics,camera,walkers))return;
        if (DrawDemolitionTerrainAndWalkers(graphics, camera, walkers)) return;
        DrawMapAndWalkersCore(graphics, camera, walkers);
    }

    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    [OriginalAddress(0x00436B60, "FUN_00436b60")]
    [OriginalAddress(0x00438E50, "FUN_00438e50")]
    private void DrawMapAndWalkersCore(Graphics graphics, CameraManager camera, Walkers walkers)
    {
        if (currentMap is null || camera.View is not LegacyCityViewState || Frame is null)
        {
            if (Frame is not null)
                graphics.DrawImageUnscaled(Frame, 0, 0);
            walkerRenderer.Draw(graphics, camera, walkers);
            return;
        }

        // The retained map frame already contains every static/base/object
        // pixel. Walkers must be inserted into its painter order, so replay
        // only later overlays inside rectangles actually touched by a walker.
        // This preserves FUN_00436B60 ordering without redrawing the terrain.
        graphics.DrawImageUnscaled(Frame, 0, 0);

        int PainterRank(Walker walker)
        {
            // FUN_00436B60 follows walker_grid for the walker's current cell.
            // Sub-tile movement changes the sprite offset in FUN_00438E50 but
            // does not move it to the destination cell's painter row early.
            return (uint)walker.Tile < tilePainterRanks.Length && tilePainterRanks[walker.Tile] >= 0
                ? tilePainterRanks[walker.Tile]
                : lastPainterRank + 1;
        }

        int nextCommand = 0;
        bool hasWalkerDamage = false;
        using Region walkerDamage = new();
        walkerDamage.MakeEmpty();

        void DrawMapThroughRank(int rank)
        {
            // The executable completes the building/object loop for every
            // column in a screen row before its walker loop for that row.
            int first = nextCommand;
            while (nextCommand < visibleCommands.Length && commandPainterRanks[nextCommand] <= rank)
                nextCommand++;
            if (!hasWalkerDamage || first == nextCommand)
                return;

            GraphicsState state = graphics.Save();
            graphics.SetClip(walkerDamage, CombineMode.Intersect);
            for (int index = first; index < nextCommand; index++)
            {
                DrawMapOverlayCommand(graphics, visibleCommands[index]);
            }
            graphics.Restore(state);
        }

        void AddWalkerDamage(Rectangle bounds)
        {
            walkerDamage.Union(bounds);
            hasWalkerDamage = true;
        }

        walkerRenderer.Draw(
            graphics, camera, walkers, PainterRank, DrawMapThroughRank, AddWalkerDamage);
        DrawMapThroughRank(int.MaxValue);
    }

    /// <summary>
    /// Recreates the marker branch in FUN_0043B630. A valid idle road cursor
    /// uses the selected road sprite as a green RGB565 AND mask; an active
    /// drag uses DAT_00F56BE4's one-cell footprint sprite as a red AND mask.
    /// </summary>
    [OriginalAddress(0x004101C0, "FUN_004101c0")]
    [OriginalAddress(0x00410700, "FUN_00410700")]
    [OriginalAddress(0x0043B630, "FUN_0043b630")]
    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    [OriginalAddress(0x0053F4F0, "FUN_0053f4f0")]
    public void ApplyRoadPlacementMarker(
        Bitmap destination,
        CameraManager camera,
        int tile,
        bool invalid,
        ushort rgb565Mask,
        Rectangle clip)
        => buildPreviewRenderer.ApplyRoadMarker(
            destination, camera, currentBuilder, tile, invalid, rgb565Mask, clip);

    [OriginalAddress(0x0043B630, "FUN_0043b630")]
    public void ApplyHousingPlacementMarker(
        Bitmap destination,
        CameraManager camera,
        int tile,
        bool invalid,
        ushort rgb565Mask,
        Rectangle clip)
        => buildPreviewRenderer.ApplyHousingMarker(
            destination, camera, currentBuilder, tile, invalid, rgb565Mask, clip);

    public void ApplySafetyServicePlacementMarker(
        Bitmap destination, CameraManager camera, int tile, int imageId,
        bool invalid, Rectangle clip) =>
        buildPreviewRenderer.ApplySafetyServiceMarker(
            destination, camera, tile, imageId, invalid, clip);

    [OriginalAddress(0x0043B630, "square-building placement feedback")]
    public void ApplySquareBuildingPlacementMarker(
        Bitmap destination, CameraManager camera, LegacySquarePlacementPreview preview, Rectangle clip) =>
        buildPreviewRenderer.ApplySquareBuildingMarker(destination,camera,currentBuilder,preview,clip);

    [OriginalAddress(0x00436B60, "DAT_00799a97 selects the 0xf863 building mask")]
    [OriginalAddress(0x004743F0, "Clear-land drag preview")]
    public void ApplyClearLandDemolitionPreview(
        Bitmap destination,
        CameraManager camera,
        IReadOnlyCollection<int> buildingIds,
        Rectangle clip)
        => buildPreviewRenderer.ApplyDemolition(
            destination, camera, currentMap, currentSession, animatedWaterImages, buildingIds, clip);

    [OriginalAddress(0x00430D30, "FUN_00430d30")]
    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    [OriginalAddress(0x0053E590, "FUN_0053e590")]
    public void Build(
        LegacyMissionSession session,
        CameraManager camera,
        int frameWidth,
        int frameHeight,
        bool compactSidebar,
        bool fullMap = false,
        bool resetView = true,
        FloodManager? floodManager = null)
    {
        terrainFrameRevision++;
        dynamicImageOverrides.Clear();
        Sg3Archive terrainArchive = graphicsAssets.LoadArchive("Pharaoh_Terrain.sg3");
        int? renderOrientation = !resetView && camera.View is not null
            ? camera.View.Orientation
            : null;
        Func<int, int>? floodplainGrowth = floodManager is null
            ? null
            : floodManager.GetGrowthStage;
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> renderChunks = session.LogicalChunks;
        LegacyGridBuilder builder = new(
            renderChunks, terrainArchive, renderOrientation,
            floodplainGrowth);
        LegacyCityMapSnapshot map = builder.BuildTerrainSnapshot();
        bool sessionChanged = !ReferenceEquals(currentSession, session);
        if (sessionChanged ||
            stableTerrainOrientation != builder.Orientation)
        {
            stableTerrainImages.Clear();
            animatedWaterImages.Clear();
            buildingVisualSignatures.Clear();
            buildingAnimationClock=new();
            currentSession = session;
            stableTerrainOrientation = builder.Orientation;
        }

        // Preserve randomized static sand/land artwork, but never cache a
        // surface owned by roads, canals, buildings or future floodplain
        // farms. Those layers can appear/disappear during inundation.
        foreach (int tile in stableTerrainImages.Keys
                     .Where(builder.IsDynamicSurfaceTile).ToArray())
            stableTerrainImages.Remove(tile);
        for (int tile = 0; tile < LegacyCityMapSnapshot.TileCount; tile++)
        {
            if (!builder.IsDynamicSurfaceTile(tile) && !stableTerrainImages.ContainsKey(tile))
                stableTerrainImages.Add(tile, map.GetImageId(tile));
        }
        if (stableTerrainImages.Count > 0)
            map = map.WithImageIds(stableTerrainImages);
        if (dynamicImageOverrides.Count > 0)
            map = map.WithImageIds(dynamicImageOverrides);
        currentBuilder = builder;
        currentMap = map;
        currentTerrainGrid = renderChunks["terrain_grid"];
        waterAnimation ??= new LegacyWaterAnimation(terrainArchive);
        currentFrameWidth = frameWidth;
        currentFrameHeight = frameHeight;
        currentCompactSidebar = compactSidebar;
        currentFullMap = fullMap;
        if (fullMap)
        {
            ReplaceFrame(BuildFullTerrainFrame(builder, map));
            return;
        }

        LegacyIsometricViewport viewport =
            LegacyIsometricViewport.ForDisplayWidth(frameWidth, compactSidebar);
        if (resetView || camera.View is null)
        {
            int orientation = BinaryPrimitives.ReadInt32LittleEndian(
                session.LogicalChunks["city_view_orientation"].Span);
            if (orientation is not (0 or 2 or 4 or 6))
                orientation = 0;
            ReadOnlySpan<byte> cameraBytes = session.LogicalChunks["city_view_camera"].Span;
            camera.Configure(
                map, viewport, orientation,
                BinaryPrimitives.ReadInt32LittleEndian(cameraBytes),
                BinaryPrimitives.ReadInt32LittleEndian(cameraBytes[sizeof(int)..]),
                reset: true);
        }
        else
        {
            camera.Configure(
                map, viewport, camera.View.Orientation,
                camera.View.CameraX, camera.View.CameraY, reset: false);
        }

        LegacyCityViewState view = camera.View!;
        RebuildVisibleCommandPlan(map, view, camera);
        Bitmap frame = BuildViewportFrame(map, view, frameWidth, frameHeight, compactSidebar);
        Bitmap? minimap = null;
        LegacyCityMinimapProjection? projection = null;
        try
        {
            projection = LegacyCityMinimapProjection.Build(renderChunks, view);
            minimap = Minimap.Build(graphicsAssets, projection, minimapSprites);
        }
        catch
        {
            frame.Dispose();
            minimap?.Dispose();
            throw;
        }

        ReplaceFrame(frame);
        MinimapFrame?.Dispose();
        MinimapFrame = minimap;
        MinimapProjection = projection;
    }

    /// <summary>
    /// Mirrors the original locked background surface: terrain changes update
    /// the existing viewport bitmap in place instead of allocating and
    /// composing a new full-screen surface for every inundation step.
    /// </summary>
    [OriginalAddress(0x004BD950, "FUN_004bd950")]
    [OriginalAddress(0x00430EF0, "FUN_00430ef0")]
    public void RefreshTerrain(
        LegacyMissionSession session,
        CameraManager camera,
        IEnumerable<int> changedTiles)
    {
        HashSet<int> affected = [.. changedTiles];
        if (affected.Count == 0 || currentFullMap || currentMap is null || currentBuilder is null ||
            Frame is null || camera.View is not LegacyCityViewState view ||
            !ReferenceEquals(currentSession, session))
            return;

        LegacyCityMapSnapshot oldMap = currentMap;
        LegacyGridBuilder builder = currentBuilder;
        bool radarChanged=builder.RefreshTerrainTiles(session.LogicalChunks["terrain_grid"].Span, affected);
        bool floodOnly=builder.CanRefreshFloodImages(affected);
        LegacyCityMapSnapshot map = floodOnly?builder.RefreshFloodImages(oldMap,affected):builder.BuildTerrainSnapshot();

        foreach (int tile in affected)
        {
            if (builder.IsDynamicSurfaceTile(tile))
                stableTerrainImages.Remove(tile);
            else if (!stableTerrainImages.ContainsKey(tile))
                stableTerrainImages[tile] = map.GetImageId(tile);
        }

        if (!floodOnly && stableTerrainImages.Count > 0)
            map = map.WithImageIds(stableTerrainImages);
        if (!floodOnly && dynamicImageOverrides.Count > 0)
            map = map.WithImageIds(dynamicImageOverrides);

        currentBuilder = builder;
        currentMap = map;
        currentTerrainGrid = session.LogicalChunks["terrain_grid"];
        if(!ReferenceEquals(oldMap,map))
        {
            if(floodOnly)RefreshVisibleImageIds(map);else RebuildVisibleCommandPlan(map,view,camera);
            PatchViewportFrame(oldMap, map, view, affected);
        }

        if(!radarChanged)return;
        LegacyCityMinimapProjection projection = LegacyCityMinimapProjection.Build(session.LogicalChunks, view);
        Bitmap minimap = Minimap.Build(graphicsAssets, projection, minimapSprites);
        MinimapFrame?.Dispose();
        MinimapFrame = minimap;
        MinimapProjection = projection;
    }

    /// <summary>
    /// Re-submits the image of a house whose population changed without
    /// rebuilding terrain, footprint, orientation, or camera data. Pharaoh's
    /// FUN_0046CEA0 follows the same narrow path through FUN_0047BF50 when an
    /// immigrant occupies an existing one-cell parcel.
    /// </summary>
    [OriginalAddress(0x0046CEA0, "FUN_0046cea0")]
    public bool RefreshHousingImages(
        LegacyMissionSession session,
        CameraManager camera,
        IEnumerable<int> changedTiles)
    {
        HashSet<int> affected = [.. changedTiles.Where(
            tile => (uint)tile < LegacyCityMapSnapshot.TileCount)];
        if (affected.Count == 0 || currentFullMap || currentMap is null || Frame is null ||
            camera.View is not LegacyCityViewState view || !ReferenceEquals(currentSession, session) ||
            !session.LogicalChunks.TryGetValue("image_grid", out ReadOnlyMemory<byte> imageGrid) ||
            imageGrid.Length != LegacyCityMapSnapshot.ImageGridLength)
            return false;

        Dictionary<int, uint> replacements = new(affected.Count);
        foreach (int tile in affected)
        {
            // A batch may contain an arrival/evolution queued before the same
            // cell burned or was cleared. Its serialized image is now zero:
            // applying it would punch a persistent hole into the terrain map.
            if (!TryGetBuilding(tile, out var liveBuilding) || liveBuilding[0] is not (1 or 3) ||
                (!((BuildingType)BinaryPrimitives.ReadInt16LittleEndian(liveBuilding[16..])).IsHousing() && BinaryPrimitives.ReadInt16LittleEndian(liveBuilding[16..]) != 73))
                continue;
            uint imageId = BinaryPrimitives.ReadUInt32LittleEndian(
                imageGrid.Span.Slice(tile * sizeof(uint), sizeof(uint)));
            if (imageId < 6) continue;
            replacements[tile] = imageId;
            dynamicImageOverrides[tile] = imageId;
            stableTerrainImages.Remove(tile);
            animatedWaterImages.Remove(tile);
        }
        if (replacements.Count == 0) return false;

        LegacyCityMapSnapshot oldMap = currentMap;
        LegacyCityMapSnapshot map = oldMap.WithImageIds(replacements);
        currentMap = map;
        RefreshVisibleImageIds(map);
        PatchViewportFrame(oldMap, map, view, affected);

        LegacyCityMinimapProjection projection =
            LegacyCityMinimapProjection.Build(session.LogicalChunks, view);
        Bitmap minimap = Minimap.Build(graphicsAssets, projection, minimapSprites);
        MinimapFrame?.Dispose();
        MinimapFrame = minimap;
        MinimapProjection = projection;
        return true;
    }

    /// <summary>
    /// Rebuilds only the camera-dependent viewport surfaces. The logical map,
    /// terrain variants and global orientation data stay retained, matching
    /// FUN_004DDDE0 followed by the normal visible-row render in the EXE.
    /// </summary>
    [OriginalAddress(0x004DDDE0, "FUN_004ddde0")]
    [OriginalAddress(0x00430D30, "FUN_00430d30")]
    public bool RefreshViewport(CameraManager camera)
    {
        if (currentFullMap || currentMap is null || currentSession is null ||
            camera.View is not LegacyCityViewState view)
            return false;

        RebuildVisibleCommandPlan(currentMap, view, camera);
        Bitmap frame = BuildViewportFrame(
            currentMap, view, currentFrameWidth, currentFrameHeight, currentCompactSidebar);
        Bitmap? minimap = null;
        LegacyCityMinimapProjection? projection = null;
        try
        {
            projection = LegacyCityMinimapProjection.Build(currentSession.LogicalChunks, view);
            minimap = Minimap.Build(graphicsAssets, projection, minimapSprites);
        }
        catch
        {
            frame.Dispose();
            minimap?.Dispose();
            throw;
        }

        ReplaceFrame(frame);
        MinimapFrame?.Dispose();
        MinimapFrame = minimap;
        MinimapProjection = projection;
        return true;
    }

    [OriginalAddress(0x0040F270, "FUN_0040f270")]
    [OriginalAddress(0x0040F2C0, "FUN_0040f2c0")]
    public bool AdvanceBuildingAnimations(CameraManager camera,long milliseconds)
    {
        if(currentFullMap||currentMap is null||currentSession is null||camera.View is not LegacyCityViewState view)return false;
        buildingAnimationMilliseconds=Math.Max(milliseconds,buildingAnimationMilliseconds);
        buildingAnimationClock.AdvanceTo(buildingAnimationMilliseconds);
        List<int> changed=[];
        foreach(var command in visibleCommands)
        {
            if(!TryGetBuilding(command.TileIndex,out var building))continue;
            Sg3ImageRecord image=graphicsAssets.GlobalImages.Resolve(command.ImageId).Image;
            if(image.SpriteCount==0&&BinaryPrimitives.ReadInt16LittleEndian(building[16..])!=71)continue;
            long signature=BuildingVisualSignature(building);
            bool dirty=!buildingVisualSignatures.TryGetValue(command.TileIndex,out long previous)||signature!=previous;
            buildingVisualSignatures[command.TileIndex]=signature;
            if(LegacyBuildingVisuals.IsAnimated(building))dirty|=buildingAnimationClock.AdvanceFrame(currentSession,command.TileIndex,image);
            if(dirty)changed.Add(command.TileIndex);
        }
        if(changed.Count==0)return false;
        PatchViewportFrame(currentMap,currentMap,view,changed);return true;
    }

    [RewriteOnly("Invalidate retained overlays when staffing or granary stock changes")]
    private static long BuildingVisualSignature(ReadOnlySpan<byte> building)
    {
        long hash=building[0]*397+BinaryPrimitives.ReadInt16LittleEndian(building[64..]);
        if(BinaryPrimitives.ReadInt16LittleEndian(building[16..]) is 71 or 191)
            for(int resource=1;resource<=8;resource++)hash=unchecked(hash*397+BinaryPrimitives.ReadInt16LittleEndian(building[(86+resource*2)..]));
        if(BinaryPrimitives.ReadInt16LittleEndian(building[16..]) == 115)
            hash=unchecked(hash*397+BinaryPrimitives.ReadInt16LittleEndian(building[60..]));
        return hash;
    }

    /// <summary>
    /// Recreates the 61 ms pulse in FUN_00430D30. Only water cells traversed by
    /// the current viewport are mutated, matching FUN_00430EF0's runtime grid updates.
    /// </summary>
    public bool AdvanceWaterAnimation(CameraManager camera,bool includeBuildings=true)
    {
        if (currentFullMap || currentMap is null || waterAnimation is null ||
            camera.View is not LegacyCityViewState view || currentTerrainGrid.IsEmpty)
            return false;

        bool buildingsChanged=includeBuildings&&AdvanceBuildingAnimations(camera,buildingAnimationMilliseconds+LegacyWaterAnimation.PulseMilliseconds);
        List<int> changedTiles = [];
        Dictionary<int, int> previousImages = [];
        foreach (LegacyImageDrawCommand command in LegacyCityMapCommandStream.EnumerateBasePass(
                     currentMap, view.Viewport, view.OrientationIndex, view.CameraX, view.CameraY))
        {
            int imageId = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
            uint terrain = BinaryPrimitives.ReadUInt32LittleEndian(
                currentTerrainGrid.Span.Slice(command.TileIndex * sizeof(uint), sizeof(uint)));
            int nextImageId = waterAnimation.AdvanceFrame(imageId, terrain);
            if (nextImageId == imageId)
                continue;
            previousImages[command.TileIndex] = imageId;
            animatedWaterImages[command.TileIndex] = nextImageId;
            changedTiles.Add(command.TileIndex);
        }

        if (changedTiles.Count == 0)
            return buildingsChanged;

        PatchViewportFrame(currentMap, currentMap, view, changedTiles, previousImages);
        return true;
    }

    private Bitmap BuildViewportFrame(
        LegacyCityMapSnapshot map,
        LegacyCityViewState view,
        int frameWidth,
        int frameHeight,
        bool compactSidebar)
    {
        Bitmap frame = new(frameWidth, frameHeight, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Black);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.SetClip(LayoutManager.CityMapBounds(frameWidth, frameHeight, compactSidebar));

            IEnumerable<LegacyImageDrawCommand> commands = ReferenceEquals(map, currentMap) &&
                visibleCommands.Length > 0
                    ? visibleCommands
                    : LegacyCityMapCommandStream.EnumerateBasePass(
                        map, view.Viewport, view.OrientationIndex, view.CameraX, view.CameraY);
            var ordered = commands.ToArray();
            foreach (LegacyImageDrawCommand command in ordered)
                DrawMapBaseCommand(graphics, command);
            foreach (LegacyImageDrawCommand command in ordered)
                DrawMapOverlayCommand(graphics, command);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    [RewriteOnly("Caches the executable's visible row traversal until camera or map topology changes")]
    private void RebuildVisibleCommandPlan(
        LegacyCityMapSnapshot map,
        LegacyCityViewState view,
        CameraManager camera)
    {
        visibleCommands = [.. LegacyCityMapCommandStream.EnumerateBasePass(
            map, view.Viewport, view.OrientationIndex, view.CameraX, view.CameraY)];
        commandPainterRanks = new int[visibleCommands.Length];
        Array.Fill(tilePainterRanks, -1);
        int currentRowY = int.MinValue;
        int currentRank = -1;
        for (int index = 0; index < visibleCommands.Length; index++)
        {
            LegacyImageDrawCommand command = visibleCommands[index];
            if (!camera.TryGetTileScreenAnchor(command.TileIndex, out Point anchor))
            {
                commandPainterRanks[index] = currentRank;
                continue;
            }
            if (anchor.Y != currentRowY)
            {
                currentRowY = anchor.Y;
                currentRank++;
            }
            commandPainterRanks[index] = currentRank;
            tilePainterRanks[command.TileIndex] = currentRank;
        }
        lastPainterRank = currentRank;
    }

    [RewriteOnly("Image replacements retain the existing visible traversal and painter ranks")]
    private void RefreshVisibleImageIds(LegacyCityMapSnapshot map)
    {
        for(int i=0;i<visibleCommands.Length;i++)
            visibleCommands[i]=visibleCommands[i] with {ImageId=checked((int)map.GetImageId(visibleCommands[i].TileIndex))};
    }

    private void PatchViewportFrame(
        LegacyCityMapSnapshot oldMap,
        LegacyCityMapSnapshot newMap,
        LegacyCityViewState view,
        IEnumerable<int> changedTiles,
        IReadOnlyDictionary<int, int>? previousImages = null)
    {
        terrainFrameRevision++;
        if (Frame is null)
            return;
        HashSet<int> changed = changedTiles as HashSet<int> ?? [.. changedTiles];
        IReadOnlyList<LegacyImageDrawCommand> commands = ReferenceEquals(newMap,currentMap)?visibleCommands:
            [..LegacyCityMapCommandStream.EnumerateBasePass(newMap,view.Viewport,view.OrientationIndex,view.CameraX,view.CameraY)];
        Rectangle dirty = Rectangle.Empty;
        foreach (LegacyImageDrawCommand command in commands)
        {
            if (!changed.Contains(command.TileIndex))
                continue;
            int oldImage = previousImages?.GetValueOrDefault(
                command.TileIndex, checked((int)oldMap.GetImageId(command.TileIndex)))
                ?? checked((int)oldMap.GetImageId(command.TileIndex));
            int newImage = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
            dirty = Union(dirty, GetAnimatedCommandBounds(command, oldImage));
            dirty = Union(dirty, GetAnimatedCommandBounds(command, newImage));
        }
        dirty = Rectangle.Intersect(dirty, LayoutManager.CityMapBounds(
            currentFrameWidth, currentFrameHeight, currentCompactSidebar));
        if (dirty.IsEmpty)
            return;

        using Graphics graphics = Graphics.FromImage(Frame);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.FillRectangle(Brushes.Black, dirty);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.SetClip(dirty);
        foreach (LegacyImageDrawCommand command in commands)
        {
            int imageId = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
            if (GetAnimatedCommandBounds(command, imageId).IntersectsWith(dirty))
                DrawMapBaseCommand(graphics, command);
        }
        foreach (LegacyImageDrawCommand command in commands)
        {
            int imageId = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
            if (GetAnimatedCommandBounds(command, imageId).IntersectsWith(dirty))
                DrawMapOverlayCommand(graphics, command);
        }
    }

    [OriginalAddress(0x00430EF0, "FUN_0053E590 isometric footprint/base submission")]
    private void DrawMapBaseCommand(Graphics graphics, LegacyImageDrawCommand command)
    {
        int imageId = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
        Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
        Rectangle bounds = GetCommandBounds(command, imageId);
        if (record.Type != 30)
        {
            if (!mapSprites.TryGetValue(imageId, out Bitmap? sprite))
            {
                sprite = RgbaBitmap.Create(graphicsAssets.DecodeGlobal(imageId));
                mapSprites.Add(imageId, sprite);
            }
            graphics.DrawImageUnscaled(sprite, bounds.X, bounds.Y);
            return;
        }

        bool marked = IsDemolitionMarked(command.TileIndex);
        EnsureIsometricLayerSprites(imageId, marked);
        Bitmap baseSprite = marked
            ? demolitionBaseSprites[imageId]
            : isometricBaseSprites[imageId];
        graphics.DrawImageUnscaled(baseSprite, bounds.X, bounds.Y);
    }

    [OriginalAddress(0x00436B60, "FUN_0053F250 row-ordered isometric upper layer")]
    private void DrawMapOverlayCommand(Graphics graphics, LegacyImageDrawCommand command)
    {
        int imageId = animatedWaterImages.GetValueOrDefault(command.TileIndex, command.ImageId);
        Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
        Rectangle bounds = GetCommandBounds(command, imageId);
        if (record.Type == 30)
        {
            bool marked = IsDemolitionMarked(command.TileIndex);
            EnsureIsometricLayerSprites(imageId, marked);
            Bitmap overlay = marked
                ? demolitionOverlaySprites[imageId]
                : isometricOverlaySprites[imageId];
            graphics.DrawImageUnscaled(overlay, bounds.X, bounds.Y);
        }
        DrawBuildingOverlays(graphics, command, imageId);
    }

    private void EnsureIsometricLayerSprites(int imageId, bool demolitionMarked)
    {
        Dictionary<int, Bitmap> bases = demolitionMarked
            ? demolitionBaseSprites
            : isometricBaseSprites;
        Dictionary<int, Bitmap> overlays = demolitionMarked
            ? demolitionOverlaySprites
            : isometricOverlaySprites;
        if (bases.ContainsKey(imageId) && overlays.ContainsKey(imageId))
            return;

        Sg3IsometricLayers layers = graphicsAssets.DecodeGlobalIsometricLayers(imageId);
        RgbaImage baseImage = demolitionMarked
            ? CreateRgb565MaskedImage(layers.Base, 0xf863)
            : layers.Base;
        RgbaImage overlayImage = demolitionMarked
            ? CreateRgb565MaskedImage(layers.Overlay, 0xf863)
            : layers.Overlay;
        bases.Add(imageId, RgbaBitmap.Create(baseImage));
        overlays.Add(imageId, RgbaBitmap.Create(overlayImage));
    }

    [OriginalAddress(0x00436B60, "FUN_00436b60")]
    [OriginalAddress(0x00442280, "FUN_00442280")]
    private void DrawBuildingOverlays(Graphics graphics,LegacyImageDrawCommand command,int imageId)
    {
        if(!TryGetBuilding(command.TileIndex,out var building)||currentSession is null)return;
        Rectangle bounds=GetCommandBounds(command,imageId);
        int frame=currentSession.LogicalChunks["sprite_grid"].Span[command.TileIndex]&127;
        foreach(var overlay in LegacyBuildingVisuals.GetOverlays(graphicsAssets,building,imageId,frame))
        {
            if(!mapSprites.TryGetValue(overlay.ImageId,out Bitmap? bitmap))
            {bitmap=RgbaBitmap.Create(graphicsAssets.DecodeGlobal(overlay.ImageId));mapSprites.Add(overlay.ImageId,bitmap);}
            graphics.DrawImageUnscaled(bitmap,bounds.X+overlay.X,bounds.Y+overlay.Y);
        }
    }

    [RewriteOnly("Bound shared building-grid lookup for animation and stock overlays")]
    private bool TryGetBuilding(int tile,out ReadOnlySpan<byte> building)
    {
        building=default;
        if(currentSession is null||(uint)tile>=LegacyCityMapSnapshot.TileCount)return false;
        int id=BinaryPrimitives.ReadInt16LittleEndian(currentSession.LogicalChunks["building_grid"].Span[(tile*2)..]);
        if(id<=0||id>=4000)return false;
        building=currentSession.LogicalChunks["buildings"].Span.Slice(id*264,264);return true;
    }

    [OriginalAddress(0x00436B60, "Building demolition marker rendering")]
    private bool IsDemolitionMarked(int tile)
    {
        if (currentSession is null || tile < 0 || tile >= LegacyCityMapSnapshot.TileCount)
            return false;
        ReadOnlySpan<byte> buildingGrid = currentSession.LogicalChunks["building_grid"].Span;
        int id = BinaryPrimitives.ReadInt16LittleEndian(
            buildingGrid.Slice(tile * sizeof(short), sizeof(short)));
        if (id <= 0 || id >= 4000)
            return false;
        ReadOnlySpan<byte> buildings = currentSession.LogicalChunks["buildings"].Span;
        return buildings[id * 264 + 191] != 0;
    }

    private static Bitmap CreateRgb565MaskedBitmap(RgbaImage source, ushort mask)
        => RgbaBitmap.Create(CreateRgb565MaskedImage(source, mask));

    private static RgbaImage CreateRgb565MaskedImage(RgbaImage source, ushort mask)
    {
        byte[] pixels = source.Pixels.ToArray();
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] == 0)
                continue;
            ushort pixel565 = (ushort)(
                ((pixels[offset] >> 3) << 11) |
                ((pixels[offset + 1] >> 2) << 5) |
                (pixels[offset + 2] >> 3));
            ushort masked = (ushort)(pixel565 & mask);
            int red5 = (masked >> 11) & 0x1f;
            int green6 = (masked >> 5) & 0x3f;
            int blue5 = masked & 0x1f;
            pixels[offset] = (byte)((red5 << 3) | (red5 >> 2));
            pixels[offset + 1] = (byte)((green6 << 2) | (green6 >> 4));
            pixels[offset + 2] = (byte)((blue5 << 3) | (blue5 >> 2));
        }
        return new RgbaImage(source.Width, source.Height, pixels);
    }

    [RewriteOnly("Include every animation frame in retained-surface damage, including flames above an empty base anchor")]
    private Rectangle GetAnimatedCommandBounds(LegacyImageDrawCommand command,int imageId)
    {
        Rectangle bounds=GetCommandBounds(command,imageId);
        Sg3ImageRecord record=graphicsAssets.GlobalImages.Resolve(imageId).Image;
        if(!TryGetBuilding(command.TileIndex,out var building))return bounds;
        int originX=bounds.X,originY=bounds.Y;
        for(int frame=1;frame<=Math.Max(1,(int)record.SpriteCount);frame++)
            foreach(var overlay in LegacyBuildingVisuals.GetOverlays(graphicsAssets,building,imageId,frame,forceAnimation:true))
            {
                Sg3ImageRecord sprite=graphicsAssets.GlobalImages.Resolve(overlay.ImageId).Image;
                bounds=Union(bounds,new Rectangle(originX+overlay.X,originY+overlay.Y,sprite.Width,sprite.Height));
            }
        return bounds;
    }

    private Rectangle GetCommandBounds(LegacyImageDrawCommand command, int imageId)
    {
        Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
        int drawX = command.X;
        int drawY = command.Y;
        if (record.Type == 30)
            (drawX, drawY) = LegacyIsometricSpritePlacement.GetCombinedBitmapTopLeft(
                drawX, drawY, record.Width, record.Height);
        return new Rectangle(drawX, drawY, record.Width, record.Height);
    }

    private static Rectangle Union(Rectangle first, Rectangle second) =>
        first.IsEmpty ? second : Rectangle.Union(first, second);

    [RewriteOnly("Diagnostic full-map composition using the verified terrain image grid")]
    private Bitmap BuildFullTerrainFrame(
        LegacyGridBuilder builder,
        LegacyCityMapSnapshot map)
    {
        List<(int ImageId, int AnchorX, int AnchorY, Sg3ImageRecord Record)> cells = [];
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        for (int diagonal = 0; diagonal <= builder.Width + builder.Height - 2; diagonal++)
        {
            int firstX = Math.Max(0, diagonal - (builder.Height - 1));
            int lastX = Math.Min(builder.Width - 1, diagonal);
            for (int x = firstX; x <= lastX; x++)
            {
                int y = diagonal - x;
                int tile = builder.Origin + x + y * LegacyCityMapSnapshot.MapLength;
                if (!builder.IsInsidePlayableArea(tile) || (map.GetEdgeFlags(tile) & 0x40) == 0)
                    continue;
                int imageId = checked((int)map.GetImageId(tile));
                if (imageId == 0)
                    continue;
                Sg3ImageRecord record = graphicsAssets.GlobalImages.Resolve(imageId).Image;
                int drawCellX = ((x - y) + builder.Height - 1) * LegacyIsometricViewport.HalfTileWidth;
                int drawCellY = (x + y) * LegacyIsometricViewport.HalfTileHeight;
                (int anchorX, int anchorY) = LegacyCityMapCommandStream.AdjustMultiTileDrawAnchor(
                    drawCellX, drawCellY, map.GetDrawAnchor(tile));
                (int drawX, int drawY) = record.Type == 30
                    ? LegacyIsometricSpritePlacement.GetCombinedBitmapTopLeft(anchorX, anchorY, record.Width, record.Height)
                    : (anchorX, anchorY);
                minX = Math.Min(minX, drawX);
                minY = Math.Min(minY, drawY);
                maxX = Math.Max(maxX, drawX + record.Width);
                maxY = Math.Max(maxY, drawY + record.Height);
                cells.Add((imageId, anchorX, anchorY, record));
            }
        }
        if (cells.Count == 0)
            throw new InvalidDataException("The playable map contains no drawable terrain cells.");

        Bitmap frame = new(maxX - minX, maxY - minY,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Dictionary<int, Bitmap> decoded = [];
        try
        {
            using Graphics graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;
            foreach ((int imageId, int anchorX, int anchorY, Sg3ImageRecord record) in cells)
            {
                (int drawX, int drawY) = record.Type == 30
                    ? LegacyIsometricSpritePlacement.GetCombinedBitmapTopLeft(anchorX, anchorY, record.Width, record.Height)
                    : (anchorX, anchorY);
                if (!decoded.TryGetValue(imageId, out Bitmap? sprite))
                {
                    sprite = RgbaBitmap.Create(graphicsAssets.DecodeGlobal(imageId));
                    decoded.Add(imageId, sprite);
                }
                graphics.DrawImageUnscaled(sprite, drawX - minX, drawY - minY);
            }
        }
        finally
        {
            foreach (Bitmap sprite in decoded.Values)
                sprite.Dispose();
        }
        return frame;
    }

    private void ReplaceFrame(Bitmap frame)
    {
        Frame?.Dispose();
        Frame = frame;
    }

    public void Dispose()
    {
        demolitionTerrainPreview.Dispose();roadTerrainPreview.Dispose();
        Frame?.Dispose();
        MinimapFrame?.Dispose();
        Frame = null;
        MinimapFrame = null;
        MinimapProjection = null;
        currentBuilder = null;
        currentSession = null;
        currentMap = null;
        currentTerrainGrid = default;
        visibleCommands = [];
        commandPainterRanks = [];
        Array.Fill(tilePainterRanks, -1);
        lastPainterRank = -1;
        buildMode.CancelAll();
        stableTerrainImages.Clear();
        animatedWaterImages.Clear();
        stableTerrainOrientation = -1;
        foreach (Bitmap sprite in mapSprites.Values)
            sprite.Dispose();
        mapSprites.Clear();
        foreach (Bitmap sprite in demolitionSprites.Values)
            sprite.Dispose();
        demolitionSprites.Clear();
        foreach (Bitmap sprite in isometricBaseSprites.Values)
            sprite.Dispose();
        isometricBaseSprites.Clear();
        foreach (Bitmap sprite in isometricOverlaySprites.Values)
            sprite.Dispose();
        isometricOverlaySprites.Clear();
        foreach (Bitmap sprite in demolitionBaseSprites.Values)
            sprite.Dispose();
        demolitionBaseSprites.Clear();
        foreach (Bitmap sprite in demolitionOverlaySprites.Values)
            sprite.Dispose();
        demolitionOverlaySprites.Clear();
        minimapSprites.Clear();
        walkerRenderer.Dispose();
        buildPreviewRenderer.Dispose();
    }
}
