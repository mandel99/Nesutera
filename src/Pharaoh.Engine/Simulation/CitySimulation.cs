using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.Environment;
using Pharaoh.Engine.Housing;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Walkers;

namespace Pharaoh.Engine.Simulation;

public readonly record struct CitySimulationAdvance(
    LegacyConstructionAdvance Construction,
    LegacyFloodAdvance Flood,
    LegacyWellAdvance Wells,
    LegacyHousingAdvance Housing,
    WalkerAdvance Walkers,
    IReadOnlyList<int> SafetyServiceChangedTiles,
    LegacyBuildingRiskAdvance BuildingRisks);

/// <summary>
/// Owns the active city simulation subsystems and preserves the verified legacy
/// dispatcher order. Presentation remains a desktop responsibility.
/// </summary>
public sealed class CitySimulation
{
    private readonly LegacyMissionSession session;
    private readonly IReadOnlyList<LegacyHousingLevel> housingLevels;
    private readonly LegacyBuildingModelCosts? buildingModels;
    private readonly LegacyFigureModels? figureModels;
    private readonly int difficulty;

    private CitySimulation(
        LegacyMissionSession session,
        IReadOnlyList<LegacyHousingLevel> housingLevels,
        LegacyBuildingModelCosts? buildingModels,
        LegacyFigureModels? figureModels,
        int difficulty)
    {
        this.session = session;
        this.housingLevels = housingLevels;
        this.buildingModels = buildingModels;
        this.figureModels = figureModels;
        this.difficulty = difficulty;
        Flood = FloodManager.Create(session);
        Wells = WellManager.Create(session);
        Housing = HousingManager.Create(session, housingLevels);
        Walkers = Pharaoh.Engine.Walkers.Walkers.Create(
            session, housingLevels, buildingModels, figureModels, difficulty);
        SafetyServices = SafetyServiceManager.Create(session, buildingModels, difficulty);
        BuildingRisks = buildingModels is null
            ? null
            : BuildingRiskManager.Create(session, buildingModels, difficulty);
        Construction = LegacyConstructionUndo.Create(session);
    }

    public FloodManager Flood { get; private set; }
    public WellManager Wells { get; private set; }
    public HousingManager Housing { get; private set; }
    public Pharaoh.Engine.Walkers.Walkers Walkers { get; private set; }
    public SafetyServiceManager SafetyServices { get; private set; }
    public BuildingRiskManager? BuildingRisks { get; }
    public LegacyConstructionUndo Construction { get; }

    public static CitySimulation Create(
        LegacyMissionSession session,
        IReadOnlyList<LegacyHousingLevel> housingLevels,
        LegacyBuildingModelCosts? buildingModels = null,
        int difficulty = 2,
        LegacyFigureModels? figureModels = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(housingLevels);
        return new CitySimulation(session, housingLevels, buildingModels, figureModels, difficulty);
    }

    [Pharaoh.Engine.ReverseEngineering.RewriteOnly("Interleave catch-up pulses so prevention, destruction and firefighting retain their dispatcher order")]
    public CitySimulationAdvance Advance(long firstLegacyTick,long legacyTicks)
    {
        if(legacyTicks<=1)return AdvanceOne(firstLegacyTick,legacyTicks);
        HashSet<int> construction=[],flood=[],flooded=[],receded=[],well=[],housing=[],walker=[],houseImages=[],safety=[],risk=[];
        bool undo=false,fire=false,collapse=false,footprint=false;
        int updated=0,expanded=0,removedHouses=0,spawned=0,arrived=0,removed=0,population=0;
        CitySimulationAdvance step=default;
        for(long i=0;i<legacyTicks;i++)
        {
            step=AdvanceOne(firstLegacyTick+i,1);
            construction.UnionWith(step.Construction.ChangedTiles);undo|=step.Construction.UndoAvailabilityChanged;
            flood.UnionWith(step.Flood.ChangedTiles);flooded.UnionWith(step.Flood.FloodedTiles);receded.UnionWith(step.Flood.RecededTiles);
            well.UnionWith(step.Wells.ChangedTiles);housing.UnionWith(step.Housing.ChangedTiles);
            updated+=step.Housing.UpdatedHouses;expanded+=step.Housing.ExpandedHouses;removedHouses+=step.Housing.RemovedHouses;footprint|=step.Housing.FootprintChanged;
            spawned+=step.Walkers.Spawned;arrived+=step.Walkers.Arrived;removed+=step.Walkers.Removed;population+=step.Walkers.PopulationDelta;
            walker.UnionWith(step.Walkers.ChangedTiles);houseImages.UnionWith(step.Walkers.HousingImageChangedTiles);safety.UnionWith(step.SafetyServiceChangedTiles);
            risk.UnionWith(step.BuildingRisks.ChangedTiles);fire|=step.BuildingRisks.FireStarted;collapse|=step.BuildingRisks.BuildingCollapsed;
        }
        return new(new(construction.Order().ToArray(),undo),new(step.Flood.State,step.Flood.ProgressRow,flood.Order().ToArray(),flooded.Order().ToArray(),receded.Order().ToArray()),
            new(well.Order().ToArray()),new(updated,expanded,removedHouses,housing.Order().ToArray()){FootprintChanged=footprint},
            new(spawned,arrived,removed,population,walker.Order().ToArray(),houseImages.Order().ToArray()),safety.Order().ToArray(),new(fire,collapse,risk.Order().ToArray()));
    }

    private CitySimulationAdvance AdvanceOne(long firstLegacyTick,long legacyTicks)
    {
        LegacyConstructionAdvance construction =
            Construction.Advance(firstLegacyTick, legacyTicks);
        if (construction.TerrainChanged)
            RebuildHousingAndWalkers();

        LegacyFloodAdvance flood = Flood.Advance(legacyTicks);
        LegacyWellAdvance wells = Wells.Advance(legacyTicks);
        LegacyHousingAdvance housing = Housing.Advance(legacyTicks);
        LegacyBuildingRiskAdvance risks = BuildingRisks?.Advance(firstLegacyTick, legacyTicks)
            ?? new LegacyBuildingRiskAdvance(false, false, []);
        IReadOnlyList<int> safety = SafetyServices.Advance(legacyTicks, Walkers);
        WalkerAdvance walkers = Walkers.Advance(legacyTicks);
        return new CitySimulationAdvance(construction, flood, wells, housing, walkers, safety, risks);
    }

    public void RebuildHousing()
    {
        // Newly placed plots must receive well access before their first
        // housing pass; the EXE recomputes this derived byte in the same
        // dispatcher cycle rather than leaving it stale until a later pulse.
        Wells.Recalculate();
        Housing = HousingManager.Create(session, housingLevels);
    }

    public void RebuildHousingAndWalkers()
    {
        RebuildHousing();
        Walkers = Pharaoh.Engine.Walkers.Walkers.Create(
            session, housingLevels, buildingModels, figureModels, difficulty);
        SafetyServices = SafetyServiceManager.Create(
            session, buildingModels, difficulty);
    }

    public void RebuildAll()
    {
        Flood = FloodManager.Create(session);
        Wells = WellManager.Create(session);
        RebuildHousingAndWalkers();
    }
}
