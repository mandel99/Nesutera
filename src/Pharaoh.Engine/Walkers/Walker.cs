namespace Pharaoh.Engine.Walkers;

/// <summary>Original Pharaoh walker ids stored in the 388-byte walker record.</summary>
public enum WalkerType : byte
{
    None = 0,
    Immigrant = 1,
    Emigrant = 2,
    Homeless = 3,
    CartPusher = 4,
    StorageTransporter = 9,
    BazaarSeller = 26,
    BazaarBuyer = 39,
    BazaarCarrier = 66,
    Recruiter = 5,
    Architect = 8,
    Fireman = 10,
    Thief = 23,
    Ostrich = 69,
    OstrichHunter = 73,
    HunterArrow = 74,
    Crocodile = 82,
    Hyena = 83,
    FarmLaborer = 85,
    Policeman = 88
}

public enum WalkerState : byte
{
    Dead = 0,
    Alive = 1
}

public enum WalkerTerrainUsage : byte
{
    Any = 0,
    Roads = 1,
    Enemy = 2,
    PreferRoads = 3,
    Walls = 4,
    Animal = 5,
    Amphibious = 6
}

/// <summary>Mutable runtime counterpart of one 388-byte legacy walker record.</summary>
public sealed class Walker
{
    internal Walker(int id, WalkerType type, int tile)
    {
        Id = id;
        Type = type;
        Tile = PreviousTile = SourceTile = tile;
    }

    public int Id { get; }
    public WalkerType Type { get; }
    public WalkerState State { get; internal set; } = WalkerState.Alive;
    public int Tile { get; internal set; }
    public int PreviousTile { get; internal set; }
    public int SourceTile { get; internal set; }
    public int DestinationTile { get; internal set; }
    public int DestinationBuildingId { get; internal set; }
    public int TargetWalkerId { get; internal set; }
    public int HomeBuildingId { get; internal set; }
    public int ActionState { get; internal set; }
    public int WaitTicks { get; internal set; }
    public int Direction { get; internal set; }
    public int PreviousDirection { get; internal set; }
    public int ProgressOnTile { get; internal set; }
    public int AnimationFrame { get; internal set; }
    public int RerouteCounter { get; internal set; }
    public int MigrantPeople { get; internal set; }
    public int Damage { get; internal set; }
    public WalkerTerrainUsage TerrainUsage { get; internal set; }
    public int FormationId { get; internal set; }
    public int HerdIndex { get; internal set; }
    public int ClaimedByBuildingId { get; internal set; }
    public int AnimationTick { get; internal set; }
    public int AnimationPeriod { get; internal set; } = 8;
    public int ProjectileAge { get; internal set; }
    public int Cargo { get; internal set; }
    public bool FreeWalkInitialized { get; internal set; }
    public int FreeWalkX { get; internal set; }
    public int FreeWalkY { get; internal set; }
    internal int FreeWalkTargetX { get; set; }
    internal int FreeWalkTargetY { get; set; }
    internal int FreeWalkRemainingX { get; set; }
    internal int FreeWalkRemainingY { get; set; }
    internal int FreeWalkError { get; set; }
    internal int FreeWalkAxis { get; set; }
    public int Resource { get; internal set; }
    public int RequestedResource { get; internal set; }
    public int PredecessorId { get; internal set; }
    public bool IsHidden { get; internal set; }
    public byte ShoppingStops { get; internal set; }
    public int[] ShoppingResources { get; } = [-1, -1, -1, -1];
    public int[] ShoppingHistory { get; } = new int[4];
    public int[] ShoppingCommitted { get; } = new int[4];
    public int FireSearchTicks { get; internal set; }
    public int FireFightingFrame { get; internal set; }
    public int RoamTicks { get; internal set; }
    public bool RoamInitialized { get; internal set; }
    public bool RoamFree { get; internal set; }
    public byte RoamStepCounter { get; internal set; }
    public sbyte RoamTurnDirection { get; internal set; } = 2;
    public sbyte RoamTurnCountdown { get; internal set; } = -1;
    public IReadOnlyList<int> Route => route;
    public int RoutePosition { get; internal set; }

    internal int ReservedDistributionDestination { get; set; }
    internal List<int> route = [];
    internal int SerializedRouteId { get; set; }
    internal byte[]? NativeRecord { get; set; }
}
