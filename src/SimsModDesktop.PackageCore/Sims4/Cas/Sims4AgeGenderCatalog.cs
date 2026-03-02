namespace SimsModDesktop.PackageCore;

public static class Sims4AgeGenderCatalog
{
    private const uint Baby = 0x00000001;
    private const uint Infant = 0x00000080;
    private const uint Toddler = 0x00000002;
    private const uint Child = 0x00000004;
    private const uint Teen = 0x00000008;
    private const uint YoungAdult = 0x00000010;
    private const uint Adult = 0x00000020;
    private const uint Elder = 0x00000040;
    private const uint Male = 0x00001000;
    private const uint Female = 0x00002000;

    public static string Describe(uint ageGenderFlags)
    {
        var tokens = GetSearchTokens(ageGenderFlags);
        return tokens.Count == 0 ? "Unspecified" : string.Join(", ", tokens);
    }

    public static IReadOnlyList<string> GetSearchTokens(uint ageGenderFlags)
    {
        var tokens = new List<string>();
        AddAge(tokens, ageGenderFlags, Baby, "Baby");
        AddAge(tokens, ageGenderFlags, Infant, "Infant");
        AddAge(tokens, ageGenderFlags, Toddler, "Toddler");
        AddAge(tokens, ageGenderFlags, Child, "Child");
        AddAge(tokens, ageGenderFlags, Teen, "Teen");
        AddAge(tokens, ageGenderFlags, YoungAdult, "YoungAdult");
        AddAge(tokens, ageGenderFlags, Adult, "Adult");
        AddAge(tokens, ageGenderFlags, Elder, "Elder");

        if ((ageGenderFlags & Male) != 0)
        {
            tokens.Add("Male");
        }

        if ((ageGenderFlags & Female) != 0)
        {
            tokens.Add("Female");
        }

        if ((ageGenderFlags & (Male | Female)) == (Male | Female))
        {
            tokens.Add("Unisex");
        }

        return tokens;
    }

    private static void AddAge(List<string> tokens, uint flags, uint mask, string label)
    {
        if ((flags & mask) != 0)
        {
            tokens.Add(label);
        }
    }
}
