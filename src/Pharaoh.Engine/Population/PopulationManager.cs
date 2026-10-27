using System.Buffers.Binary;
using Pharaoh.Engine.Loading;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Population;

/// <summary>Owns the original city's periodic immigration/emigration decision.</summary>
public sealed class PopulationManager(LegacyMissionSession session)
{
    public const int MigrationDispatcherPeriod = 51;

    private const int CityPopulationOffset = 18_940;
    private const int CitySentimentOffset = 18_920;
    private const int ImmigrationAmountOffset = 28_864;
    private const int EmigrationAmountOffset = 28_868;
    private const int EmigrationCarryOffset = 28_872;
    private const int ImmigrationCarryOffset = 28_876;
    private const int MigrationPercentageOffset = 28_932;
    private const int EmigrationCooldownOffset = 28_940;
    private const int ImmigrationCooldownOffset = 28_944;
    private const int ImmigrationBlockCountAOffset = 36_876;
    private const int ImmigrationBlockCountBOffset = 36_892;

    private long migrationTicks;

    public int AdvanceMigration(
        Func<int, int> spawnImmigrants,
        Func<int, int> spawnEmigrants)
    {
        migrationTicks++;
        return migrationTicks % MigrationDispatcherPeriod == 0
            ? DispatchMigration(spawnImmigrants, spawnEmigrants)
            : 0;
    }

    [OriginalAddress(0x00458540, "FUN_00458540")]
    [OriginalAddress(0x004E8F80, "FUN_004e8f80")]
    private int DispatchMigration(
        Func<int, int> spawnImmigrants,
        Func<int, int> spawnEmigrants)
    {
        ReadOnlyMemory<byte> memory = Required("city_data", ImmigrationCooldownOffset + sizeof(int));
        byte[] city = memory.ToArray();
        int sentiment = BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(CitySentimentOffset));
        int percentage = sentiment switch
        {
            < 21 => -50,
            < 31 => -25,
            < 41 => -10,
            < 50 => 0,
            < 61 => 50,
            < 71 => 75,
            _ => 100
        };
        BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(MigrationPercentageOffset), percentage);
        BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(ImmigrationAmountOffset), 0);
        BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(EmigrationAmountOffset), 0);

        int population = BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(CityPopulationOffset));
        if (population > 199_999)
        {
            percentage = 0;
            BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(MigrationPercentageOffset), 0);
        }
        city.AsSpan(28_920, 12).Clear();
        int immigrationBlocks = memory.Length >= ImmigrationBlockCountBOffset + sizeof(int)
            ? BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(ImmigrationBlockCountAOffset)) +
              BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(ImmigrationBlockCountBOffset))
            : 0;
        if (percentage > 0 && immigrationBlocks > 3)
        {
            percentage = 0;
            BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(MigrationPercentageOffset), 0);
        }
        if (population <= 199_999)
        {
            if (percentage > 0)
            {
                int cooldown = BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(ImmigrationCooldownOffset));
                if (cooldown > 0)
                    BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(ImmigrationCooldownOffset), cooldown - 1);
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        city.AsSpan(ImmigrationAmountOffset), 12 * percentage / 100);
                    BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(EmigrationCooldownOffset), 2);
                }
            }
            else if (percentage < 0)
            {
                int cooldown = BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(EmigrationCooldownOffset));
                if (cooldown > 0)
                    BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(EmigrationCooldownOffset), cooldown - 1);
                else if (population >= 101)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        city.AsSpan(EmigrationAmountOffset), 12 * -percentage / 100);
                    BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(ImmigrationCooldownOffset), 2);
                }
            }
        }

        int immigration = ConsumeMigrationAmount(city, ImmigrationAmountOffset, ImmigrationCarryOffset);
        int emigration = ConsumeMigrationAmount(city, EmigrationAmountOffset, EmigrationCarryOffset);
        session.ReplaceLogicalChunk("city_data", city);
        int spawned = 0;
        if (immigration > 0)
            spawned += spawnImmigrants(immigration);
        if (emigration > 0)
            spawned += spawnEmigrants(emigration);
        return spawned;
    }

    [OriginalAddress(0x00458540, "FUN_00458540")]
    private static int ConsumeMigrationAmount(byte[] city, int amountOffset, int carryOffset)
    {
        int amount = BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(amountOffset));
        int dispatch = 0;
        if (amount > 0)
        {
            if (amount < 4)
            {
                amount += BinaryPrimitives.ReadInt32LittleEndian(city.AsSpan(carryOffset));
                if (amount < 4)
                    BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(carryOffset), amount);
                else
                {
                    dispatch = amount;
                    BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(carryOffset), 0);
                }
            }
            else
                dispatch = amount;
        }
        BinaryPrimitives.WriteInt32LittleEndian(city.AsSpan(amountOffset), 0);
        return dispatch;
    }

    private ReadOnlyMemory<byte> Required(string name, int minimumLength)
    {
        if (!session.LogicalChunks.TryGetValue(name, out ReadOnlyMemory<byte> memory) ||
            memory.Length < minimumLength)
            throw new InvalidDataException($"Missing or truncated mission chunk '{name}'.");
        return memory;
    }
}
