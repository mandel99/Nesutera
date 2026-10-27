using Pharaoh.Assets.Graphics;
using Pharaoh.Engine.Rendering;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.Walkers;
using Pharaoh.Desktop.Legacy.Managers;

namespace Pharaoh.Desktop.Legacy.Rendering;

/// <summary>Renders active legacy walkers without owning simulation state.</summary>
internal sealed class WalkerRenderer(Sg3AssetCatalog graphicsAssets) : IDisposable
{
    private readonly Dictionary<int, Bitmap> sprites = [];
    private readonly List<Walker> drawOrder = [];
    private LegacyWalkerAnimation? immigrantWalkAnimation;
    private LegacyWalkerAnimation? emigrantWalkAnimation;
    private LegacyWalkerAnimation? homelessWalkAnimation;
    private readonly Dictionary<WalkerType, LegacyWalkerAnimation> safetyAnimations = [];

    [OriginalAddress(0x00438E50, "FUN_00438e50")]
    [OriginalAddress(0x0049BD40, "prey artwork")]
    [OriginalAddress(0x004A2430, "hunter artwork")]
    [OriginalAddress(0x0048B4A0, "cart cargo layer")]
    [OriginalAddress(0x0048B620, "cart and cargo offsets")]
    public void Draw(
        Graphics graphics,
        CameraManager camera,
        Walkers walkers,
        Func<Walker, int>? painterRank = null,
        Action<int>? drawMapThroughRank = null,
        Action<Rectangle>? walkerDrawn = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(walkers);
        Sg3Archive archive = graphicsAssets.LoadArchive(LegacyWalkerAnimationCatalog.MainArchive);
        LegacyWalkerAnimation immigrantWalk = immigrantWalkAnimation ??=
            LegacyWalkerAnimationCatalog.ImmigrantWalk(archive);
        LegacyWalkerAnimation emigrantWalk = emigrantWalkAnimation ??=
            LegacyWalkerAnimationCatalog.EmigrantWalk(archive);
        LegacyWalkerAnimation homelessWalk = homelessWalkAnimation ??=
            LegacyWalkerAnimationCatalog.HomelessWalk(archive);

        drawOrder.Clear();
        foreach (Walker walker in walkers.Active)
            if ((walker.Type == WalkerType.Immigrant || walker.Type == WalkerType.Emigrant ||
                 walker.Type == WalkerType.Homeless ||
                 walker.Type == WalkerType.Architect || walker.Type == WalkerType.Fireman ||
                 walker.Type == WalkerType.Policeman || walker.Type == WalkerType.Ostrich ||
                 walker.Type == WalkerType.OstrichHunter || walker.Type == WalkerType.HunterArrow ||
                 walker.Type == WalkerType.Recruiter || walker.Type == WalkerType.CartPusher ||
                 walker.Type is WalkerType.StorageTransporter or WalkerType.BazaarBuyer or WalkerType.BazaarSeller or WalkerType.BazaarCarrier) && walker.State == WalkerState.Alive && !walker.IsHidden)
                drawOrder.Add(walker);
        drawOrder.Sort((left, right) =>
        {
            int depth = (painterRank?.Invoke(left) ?? left.Tile)
                .CompareTo(painterRank?.Invoke(right) ?? right.Tile);
            return depth != 0 ? depth : left.Id.CompareTo(right.Id);
        });
        foreach (Walker walker in drawOrder)
        {
            drawMapThroughRank?.Invoke(painterRank?.Invoke(walker) ?? walker.Tile);
            if (!camera.TryGetTileScreenAnchor(walker.Tile, out Point anchor))
                continue;
            float footX = anchor.X + LegacyIsometricViewport.HalfTileWidth + 1;
            float footY = anchor.Y + LegacyIsometricViewport.TileHeight;
            if (walker.FreeWalkInitialized)
            {
                float fx = walker.FreeWalkX % 15 / 15f, fy = walker.FreeWalkY % 15 / 15f;
                if (camera.TryGetTileScreenAnchor(walker.Tile + 1, out Point across))
                { footX += (across.X - anchor.X) * fx; footY += (across.Y - anchor.Y) * fx; }
                if (camera.TryGetTileScreenAnchor(walker.Tile + 228, out Point down))
                { footX += (down.X - anchor.X) * fy; footY += (down.Y - anchor.Y) * fy; }
            }
            else if (walker.RoutePosition < walker.Route.Count &&
                camera.TryGetTileScreenAnchor(walker.Route[walker.RoutePosition], out Point nextAnchor))
            {
                float progress = walker.ProgressOnTile / (float)Walkers.MovementUnitsPerTile;
                footX += (nextAnchor.X - anchor.X) * progress;
                footY += (nextAnchor.Y - anchor.Y) * progress;
            }

            int mapOrientation = camera.View?.Orientation ?? 0;
            if (walker.Type is WalkerType.Immigrant or WalkerType.Emigrant or WalkerType.Homeless)
            { footX += 1; footY -= walker.Type == WalkerType.Immigrant ? 3 : 5; }
            bool distribution = walker.Type is WalkerType.StorageTransporter or WalkerType.BazaarBuyer or WalkerType.BazaarSeller or WalkerType.BazaarCarrier;
            if (walker.Type == WalkerType.CartPusher || distribution)
            {
                // FUN_00438E50: default figure origin is (29+2,15+10).
                footX += 1; footY -= 5;
            }
            if (distribution || walker.Type is WalkerType.Ostrich or WalkerType.OstrichHunter or WalkerType.HunterArrow or WalkerType.Recruiter or WalkerType.CartPusher)
            {
                int imageId = distribution ? LegacyWalkerAnimationCatalog.DistributionImage(archive, walker, mapOrientation) : LegacyWalkerAnimationCatalog.HuntingImage(archive, walker, mapOrientation);
                if (imageId > 0 && imageId < archive.Images.Count)
                {
                    Rectangle huntingBounds = Rectangle.Empty;
                    if (walker.Type == WalkerType.HunterArrow)
                        huntingBounds = DrawSprite(graphics, archive, archive.GroupImageIds[1] +
                            ((walker.Direction - mapOrientation * 4 + 32) % 32), footX, footY);
                    if (walker.Type is WalkerType.CartPusher or WalkerType.StorageTransporter && walker.ActionState != 4)
                    {
                        var cart = LegacyCartVisuals.ResourceCart(archive, walker.Direction, mapOrientation, walker.Cargo, walker.Type == WalkerType.CartPusher ? 8 : walker.Resource);
                        if (cart.DrawAfterPusher)
                        {
                            huntingBounds = DrawSprite(graphics, archive, imageId, footX, footY);
                            huntingBounds = Rectangle.Union(huntingBounds, DrawSprite(graphics, archive, cart.ImageId, footX + cart.X, footY + cart.Y));
                        }
                        else
                        {
                            huntingBounds = DrawSprite(graphics, archive, cart.ImageId, footX + cart.X, footY + cart.Y);
                            huntingBounds = Rectangle.Union(huntingBounds, DrawSprite(graphics, archive, imageId, footX, footY));
                        }
                    }
                    else
                    {
                        Rectangle body = DrawSprite(graphics, archive, imageId, footX, footY);
                        huntingBounds = huntingBounds.IsEmpty ? body : Rectangle.Union(huntingBounds, body);
                    }
                    walkerDrawn?.Invoke(huntingBounds);
                }
                continue;
            }
            LegacyWalkerAnimation walk;
            if (walker.Type == WalkerType.Emigrant)
                walk = emigrantWalk;
            else if (walker.Type == WalkerType.Homeless)
                walk = homelessWalk;
            else if (walker.Type == WalkerType.Immigrant)
                walk = immigrantWalk;
            else if(walker.Type==WalkerType.Fireman && walker.ActionState==Walkers.FireFightingAction)
                walk=LegacyWalkerAnimationCatalog.SafetyWalk(archive,walker);
            else if (!safetyAnimations.TryGetValue(walker.Type, out walk))
            {
                walk = LegacyWalkerAnimationCatalog.SafetyWalk(archive, walker.Type);
                safetyAnimations.Add(walker.Type, walk);
            }
            Rectangle bounds = DrawSprite(graphics, archive,
                LegacyWalkerAnimationCatalog.ImageFor(walk, walker, mapOrientation), footX, footY);
            walkerDrawn?.Invoke(bounds);
        }
    }

    [OriginalAddress(0x004FA780,"FUN_004fa780")]
    [OriginalAddress(0x0040F9A0,"FUN_0040f9a0")]
    public void DrawInspection(Graphics graphics,Walker walker,Rectangle bounds)
    {
        Sg3Archive archive=graphicsAssets.LoadArchive(LegacyWalkerAnimationCatalog.MainArchive);
        int image;
        if(walker.Type is WalkerType.Ostrich or WalkerType.OstrichHunter or WalkerType.HunterArrow or WalkerType.CartPusher or WalkerType.Recruiter)
            image=LegacyWalkerAnimationCatalog.HuntingImage(archive,walker,0);
        else if(walker.Type is WalkerType.StorageTransporter or WalkerType.BazaarBuyer or WalkerType.BazaarSeller or WalkerType.BazaarCarrier)
            image=LegacyWalkerAnimationCatalog.DistributionImage(archive,walker,0);
        else
        {
            if(walker.Type is not (WalkerType.Immigrant or WalkerType.Emigrant or WalkerType.Homeless or WalkerType.Architect or WalkerType.Fireman or WalkerType.Policeman))return;
            var animation=walker.Type switch
            {
                WalkerType.Immigrant=>LegacyWalkerAnimationCatalog.ImmigrantWalk(archive),
                WalkerType.Emigrant=>LegacyWalkerAnimationCatalog.EmigrantWalk(archive),
                WalkerType.Homeless=>LegacyWalkerAnimationCatalog.HomelessWalk(archive),
                _=>LegacyWalkerAnimationCatalog.SafetyWalk(archive,walker.Type)
            };
            image=LegacyWalkerAnimationCatalog.ImageFor(animation,walker,0);
        }
        if(image<=0||image>=archive.Images.Count)return;
        var state=graphics.Save();graphics.SetClip(bounds);
        DrawSprite(graphics,archive,image,bounds.Left+bounds.Width/2,bounds.Bottom-4);graphics.Restore(state);
    }

    private Rectangle DrawSprite(
        Graphics graphics,
        Sg3Archive archive,
        int imageIndex,
        float footX,
        float footY)
    {
        Sg3ImageRecord record = archive.Images[imageIndex];
        if (!sprites.TryGetValue(imageIndex, out Bitmap? sprite))
        {
            sprite = RgbaBitmap.Create(graphicsAssets.Decode(
                LegacyWalkerAnimationCatalog.MainArchive, imageIndex));
            sprites.Add(imageIndex, sprite);
        }
        int x = (int)MathF.Round(footX - record.SpriteOffsetX);
        int y = (int)MathF.Round(footY - record.SpriteOffsetY);
        graphics.DrawImageUnscaled(sprite, x, y);
        return new Rectangle(x, y, sprite.Width, sprite.Height);
    }

    public void Dispose()
    {
        foreach (Bitmap sprite in sprites.Values)
            sprite.Dispose();
        sprites.Clear();
    }
}
