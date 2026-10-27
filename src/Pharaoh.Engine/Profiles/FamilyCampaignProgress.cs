using System.Buffers.Binary;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.Profiles;

public sealed record FamilyCampaignProgress(
    int CompletedMissionCount,
    int NextPeriod,
    IReadOnlySet<int> CompletedMissionIds)
{
    private const int RecordSize = 0x40;
    private const int RecordCountOffset = 0x1900;
    private const int MissionIdOffset = 8;
    private const int CompletedOffset = 0x3c;

    [RewriteOnly("Atomically patches only known mission completion bytes in an existing original family file")]
    public static void MarkCompleted(string path, int missionId)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < RecordCountOffset + sizeof(int))
            throw new InvalidDataException("The family campaign record table is truncated.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(RecordCountOffset));
        if (count is < 0 or > 100)
            throw new InvalidDataException("The family campaign record count is invalid.");
        bool found = false;
        for (int index = 0; index < count; index++)
        {
            int offset = index * RecordSize;
            if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + MissionIdOffset)) != missionId) continue;
            data[offset + CompletedOffset] = 1;
            found = true;
        }
        if (!found) throw new InvalidDataException("The mission is absent from the family campaign table.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static FamilyCampaignProgress Load(string path)
    {
        if (!File.Exists(path))
            return new FamilyCampaignProgress(0, 0, new HashSet<int>());
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < RecordCountOffset + sizeof(int))
            return new FamilyCampaignProgress(0, 0, new HashSet<int>());

        int count = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(RecordCountOffset)), 0,
            Math.Min(100, RecordCountOffset / RecordSize));
        int completed = 0;
        int nextPeriod = 0;
        HashSet<int> completedMissionIds = [];
        bool foundIncomplete = false;
        for (int period = 0; period < 9; period++)
        {
            bool periodHasMission = false;
            bool periodIncomplete = false;
            for (int index = 0; index < count; index++)
            {
                int offset = index * RecordSize;
                if (data[offset] != period) continue;
                int missionId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + MissionIdOffset));
                if (missionId < 0) continue;
                periodHasMission = true;
                if (data[offset + CompletedOffset] == 0) periodIncomplete = true;
                else
                {
                    completed++;
                    completedMissionIds.Add(missionId);
                }
            }
            if (!foundIncomplete && periodHasMission && periodIncomplete)
            {
                nextPeriod = period;
                foundIncomplete = true;
            }
        }
        return new FamilyCampaignProgress(completed, nextPeriod, completedMissionIds);
    }
}
