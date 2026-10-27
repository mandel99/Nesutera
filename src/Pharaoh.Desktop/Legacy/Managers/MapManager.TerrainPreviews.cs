using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Walkers;

namespace Pharaoh.Desktop.Legacy.Managers;

internal sealed partial class MapManager
{
    private long terrainFrameRevision;
    private readonly TerrainPreview roadTerrainPreview=new(),demolitionTerrainPreview=new();

    [RewriteOnly("Detached substitutions share the normal terrain/object/walker painter order")]
    private sealed class TerrainPreview : IDisposable
    {
        public LegacyCityMapSnapshot? SourceMap,Map;
        public LegacyGridBuilder? Builder;
        public Bitmap? SourceFrame,Frame;
        public int[] Selection=[],Changed=[];
        public LegacyImageDrawCommand[] Commands=[];
        public int[] Ranks=[],TileRanks=[];
        public int LastRank;
        public long FrameRevision=-1;
        public void Dispose(){Frame?.Dispose();Frame=null;}
    }

    [OriginalAddress(0x00474700,"FUN_00474700")]
    [RewriteOnly("Caches the selected terrain independently of animation surface revisions")]
    private bool DrawDemolitionTerrainAndWalkers(Graphics graphics,CameraManager camera,Walkers walkers)
    {
        var placement=buildMode.ClearLandPlacement;
        if(!placement.IsActive||placement.PreviewTiles.Count==0||currentMap is null||currentBuilder is null||
            currentSession is null||Frame is null||camera.View is not LegacyCityViewState view)return false;
        var cache=demolitionTerrainPreview;
        if(!ReferenceEquals(cache.SourceMap,currentMap)||!ReferenceEquals(cache.SourceFrame,Frame)||
            !placement.PreviewTiles.SequenceEqual(cache.Selection))
        {
            bool sameSource=ReferenceEquals(cache.SourceMap,currentMap)&&ReferenceEquals(cache.SourceFrame,Frame);
            int[] oldChanged=cache.Changed;
            cache.SourceMap=currentMap;cache.SourceFrame=Frame;cache.Selection=placement.PreviewTiles.ToArray();
            cache.Map=null;cache.FrameRevision=-1;
            var result=placement.PreviewTerrain();cache.Changed=result.ChangedTiles.ToArray();
            if(cache.Changed.Length==0)cache.Builder=null;
            if(cache.Changed.Length>0)
            {
                if(!sameSource||cache.Builder is null)
                {
                    var chunks=currentSession.LogicalChunks.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
                    chunks["terrain_grid"]=result.TerrainGrid;
                    cache.Builder=currentBuilder.CreatePreview(chunks,graphicsAssets.LoadArchive("Pharaoh_Terrain.sg3"));
                }
                else cache.Builder.RefreshTerrainTiles(result.TerrainGrid.Span,oldChanged.Concat(cache.Changed));
                HashSet<int> affected=[];
                foreach(int tile in cache.Changed)
                for(int dy=-5;dy<=5;dy++)for(int dx=-5;dx<=5;dx++)
                {int x=tile%228+dx,y=tile/228+dy;if((uint)x<228&&(uint)y<228)affected.Add(y*228+x);}
                cache.Map=cache.Builder.RefreshClearedTerrainImages(currentMap,affected);PreparePreviewPlan(cache,view,camera);
            }
        }
        return DrawTerrainPreview(graphics,camera,walkers,cache);
    }

    [OriginalAddress(0x00430EF0,"Temporary road images use the normal terrain pass")]
    [OriginalAddress(0x00436B60,"Walkers follow the road base pass")]
    private bool DrawRoadTerrainAndWalkers(Graphics graphics,CameraManager camera,Walkers walkers)
    {
        var placement=buildMode.RoadPlacement;
        if(!placement.IsActive||placement.PreviewTiles.Count==0||currentMap is null||currentBuilder is null||
            Frame is null||camera.View is not LegacyCityViewState)return false;
        var cache=roadTerrainPreview;
        if(!ReferenceEquals(cache.SourceMap,currentMap)||!ReferenceEquals(cache.SourceFrame,Frame)||
            !placement.PreviewTiles.SequenceEqual(cache.Selection))
        {
            cache.SourceMap=currentMap;cache.SourceFrame=Frame;cache.Selection=placement.PreviewTiles.ToArray();cache.FrameRevision=-1;
            HashSet<int> roads=[..cache.Selection],affected=[..roads];
            foreach(int tile in roads)foreach(int offset in new[]{-228,1,228,-1})
                if(currentBuilder.IsRoadTile(tile+offset))affected.Add(tile+offset);
            Dictionary<int,uint> images=[];
            foreach(int tile in affected)images[tile]=checked((uint)currentBuilder.ResolveRoadPreviewImage(tile,roads));
            // Roads do not change draw anchors. Substitute their command image
            // IDs without copying the 228x228 image grid for each mouse move.
            cache.Changed=affected.ToArray();cache.Map=currentMap;
            cache.Commands=visibleCommands.Select(c=>images.TryGetValue(c.TileIndex,out uint image)?c with{ImageId=(int)image}:c).ToArray();
            cache.Ranks=commandPainterRanks;cache.TileRanks=tilePainterRanks;cache.LastRank=lastPainterRank;
        }
        return DrawTerrainPreview(graphics,camera,walkers,cache);
    }

    [RewriteOnly("Preview topology computes its visible plan once per selected rectangle")]
    private void PreparePreviewPlan(TerrainPreview cache,LegacyCityViewState view,CameraManager camera)
    {
        var commands=visibleCommands;var ranks=commandPainterRanks;var tiles=tilePainterRanks;int last=lastPainterRank;
        try
        {
            tilePainterRanks=new int[LegacyCityMapSnapshot.TileCount];RebuildVisibleCommandPlan(cache.Map!,view,camera);
            cache.Commands=visibleCommands;cache.Ranks=commandPainterRanks;cache.TileRanks=tilePainterRanks;cache.LastRank=lastPainterRank;
        }
        finally{visibleCommands=commands;commandPainterRanks=ranks;tilePainterRanks=tiles;lastPainterRank=last;}
    }

    [RewriteOnly("Patch temporary terrain below live walkers while retaining the animated background")]
    private bool DrawTerrainPreview(Graphics graphics,CameraManager camera,Walkers walkers,TerrainPreview cache)
    {
        if(cache.Map is null||Frame is null)return false;
        var map=currentMap;var frame=Frame;var commands=visibleCommands;var ranks=commandPainterRanks;
        var tiles=tilePainterRanks;int last=lastPainterRank;Dictionary<int,int> removed=[];
        foreach(int tile in cache.Changed)if(animatedWaterImages.Remove(tile,out int image))removed[tile]=image;
        try
        {
            currentMap=cache.Map;visibleCommands=cache.Commands;commandPainterRanks=cache.Ranks;
            tilePainterRanks=cache.TileRanks;lastPainterRank=cache.LastRank;
            if(cache.FrameRevision!=terrainFrameRevision)
            {
                if(cache.Frame?.Size!=frame.Size){cache.Dispose();cache.Frame=new Bitmap(frame.Width,frame.Height);}
                using(var g=Graphics.FromImage(cache.Frame!)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(frame,0,0);}
                Frame=cache.Frame;long revision=terrainFrameRevision;
                PatchViewportFrame(map!,cache.Map,camera.View!,cache.Changed);
                terrainFrameRevision=revision;cache.FrameRevision=revision;
            }
            Frame=cache.Frame;DrawMapAndWalkersCore(graphics,camera,walkers);
        }
        finally
        {
            currentMap=map;Frame=frame;visibleCommands=commands;commandPainterRanks=ranks;
            tilePainterRanks=tiles;lastPainterRank=last;
            foreach(var entry in removed)animatedWaterImages[entry.Key]=entry.Value;
        }
        return true;
    }
}
