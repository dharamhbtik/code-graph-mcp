namespace CodeGraphMcp.Core.Utilities;

public static class TokenEstimator
{
    // Rough heuristic: 1 token ≈ 4 characters (conservative for code)
    public static int Estimate(string text) => text.Length / 4;
    public static int Estimate(int charCount) => charCount / 4;
}
