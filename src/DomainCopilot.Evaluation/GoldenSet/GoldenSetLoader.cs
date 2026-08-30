using System.Text.Json;

namespace DomainCopilot.Evaluation.GoldenSet;

public static class GoldenSetLoader
{
    public static List<GoldenSetItem> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Golden set file not found at '{path}'. Pass --golden-set <path> pointing at eval/golden-set.json.");
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        GoldenSetFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GoldenSetFile>(json, options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Golden set file at '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (parsed is null || parsed.GoldenSet.Count == 0)
        {
            throw new InvalidDataException(
                $"Golden set file at '{path}' parsed but contained no items under a 'golden_set' array.");
        }

        var duplicateIds = parsed.GoldenSet
            .GroupBy(i => i.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            throw new InvalidDataException($"Golden set has duplicate ids: {string.Join(", ", duplicateIds)}");
        }

        foreach (var item in parsed.GoldenSet)
        {
            // Touching .ExpectedBehavior forces the enum parse now, at load time,
            // rather than failing mid-run on whichever item happens to be malformed.
            _ = item.ExpectedBehavior;

            if (item.ExpectedSourceDocuments.Count == 0)
            {
                throw new InvalidDataException(
                    $"Golden set item '{item.Id}' has an empty expected_source_documents list.");
            }
        }

        return parsed.GoldenSet;
    }
}
