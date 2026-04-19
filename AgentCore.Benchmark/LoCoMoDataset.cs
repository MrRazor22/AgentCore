using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCore.Benchmark;

public sealed record QA(string Question, string GroundTruth, int Category);

public sealed class LoCoMoSample
{
    public string SampleId { get; set; } = "";
    public string SpeakerA { get; set; } = "";
    public string SpeakerB { get; set; } = "";
    public List<Session> Sessions { get; set; } = new();
    public List<QA> QAs { get; set; } = new();
}

public sealed class Session
{
    public int SessionNum { get; set; }
    public string Timestamp { get; set; } = "";
    public List<Turn> Turns { get; set; } = new();
}

public sealed class Turn
{
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
}

public static class LoCoMoDataset
{
    public static List<LoCoMoSample> Load(string path)
    {
        var result = new List<LoCoMoSample>();
        var jsonText = File.ReadAllText(path);
        var doc = JsonDocument.Parse(jsonText);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var sampleId = element.GetProperty("sample_id").GetString() ?? "";
            var convNode = element.GetProperty("conversation");
            var qaNode = element.GetProperty("qa");

            var speakerA = convNode.GetProperty("speaker_a").GetString() ?? "";
            var speakerB = convNode.GetProperty("speaker_b").GetString() ?? "";

            var sessions = new List<Session>();

            int i = 1;
            while (convNode.TryGetProperty($"session_{i}", out var sessionTurns))
            {
                var ts = convNode.TryGetProperty($"session_{i}_date_time", out var tsNode) ? tsNode.GetString() : $"Session {i}";
                var session = new Session { SessionNum = i, Timestamp = ts ?? "" };

                foreach (var turnEl in sessionTurns.EnumerateArray())
                {
                    session.Turns.Add(new Turn
                    {
                        Speaker = turnEl.GetProperty("speaker").GetString() ?? "",
                        Text = turnEl.GetProperty("text").GetString() ?? ""
                    });
                }
                sessions.Add(session);
                i++;
            }

            var qas = new List<QA>();
            foreach (var qaEl in qaNode.EnumerateArray())
            {
                var currentAns = qaEl.TryGetProperty("answer", out var ansNode) ? ansNode.ToString() : "not mentioned";
                qas.Add(new QA(
                    qaEl.GetProperty("question").GetString() ?? "",
                    currentAns ?? "",
                    qaEl.GetProperty("category").GetInt32()
                ));
            }

            result.Add(new LoCoMoSample
            {
                SampleId = sampleId,
                SpeakerA = speakerA,
                SpeakerB = speakerB,
                Sessions = sessions,
                QAs = qas
            });
        }

        return result;
    }
}
