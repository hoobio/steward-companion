using System.Text.Json;

namespace Steward.Core.Tests;

public sealed class CharacterSyncOutcomeTests
{
    [Fact]
    public void CharacterSyncResponse_ParsesGigagrugsShape()
    {
        var json = """
            {"accepted":1,"rejected":[{"guid":"Player-4395-11111111","reason":"not linked to you"}]}
            """;

        var response = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.CharacterSyncResponse);

        Assert.Equal(1, response!.Accepted);
        var rejection = Assert.Single(response.Rejected);
        Assert.Equal("Player-4395-11111111", rejection.CharacterGuid);
        Assert.Equal("not linked to you", rejection.Reason);
    }

    [Theory]
    [InlineData("not linked to you", "Not linked to you in the guild roster yet: ask an officer")]
    [InlineData("professions fingerprint missing", "Changed outside the game, not sent")]
    [InlineData("professions integrity check failed", "Changed outside the game, not sent")]
    [InlineData("invalid guid", "invalid guid")]
    [InlineData("duplicate guid", "duplicate guid")]
    public void CharacterSyncRejectionCopy_Describe_MapsKnownReasons(string reason, string expected)
    {
        Assert.Equal(expected, CharacterSyncRejectionCopy.Describe(reason));
    }

    [Fact]
    public void CharacterPushRecord_RoundTripsPerCharacterOutcomes()
    {
        var record = new CharacterPushRecord(
            "abc",
            DateTimeOffset.FromUnixTimeSeconds(1758260000),
            1,
            Characters: new Dictionary<string, CharacterPushOutcome>
            {
                ["Player-4395-0A1B2C3D"] = new CharacterPushOutcome(true),
                ["Player-4395-11111111"] = new CharacterPushOutcome(false, "not linked to you"),
            });

        var json = JsonSerializer.Serialize(record, CompanionJsonContext.Default.CharacterPushRecord);
        var roundTripped = JsonSerializer.Deserialize(json, CompanionJsonContext.Default.CharacterPushRecord);

        Assert.True(roundTripped!.Characters!["Player-4395-0A1B2C3D"].Accepted);
        Assert.Null(roundTripped.Characters!["Player-4395-0A1B2C3D"].Reason);
        Assert.False(roundTripped.Characters!["Player-4395-11111111"].Accepted);
        Assert.Equal("not linked to you", roundTripped.Characters!["Player-4395-11111111"].Reason);
    }

    [Fact]
    public void ProfessionsSkillSummary_Format_OmitsSecondaryProfessions()
    {
        var skills = new List<ProfessionSkill>
        {
            new("First Aid", 1, 75, true),
            new("Blacksmithing", 7, 75, false),
            new("Mining", 8, 75, false),
        };

        Assert.Equal("Blacksmithing 7/75, Mining 8/75", ProfessionsSkillSummary.Format(skills));
    }

    [Fact]
    public void ProfessionsSkillSummary_Format_OmitsTheFraction_WhenRankIsMissing()
    {
        var skills = new List<ProfessionSkill> { new("Cooking", null, null, false) };

        Assert.Equal("Cooking", ProfessionsSkillSummary.Format(skills));
    }

    [Fact]
    public void ProfessionsSkillSummary_Format_IsEmpty_WhenThereAreNoSkills()
    {
        Assert.Equal(string.Empty, ProfessionsSkillSummary.Format(null));
        Assert.Equal(string.Empty, ProfessionsSkillSummary.Format([]));
    }

    [Fact]
    public void ProfessionsSkillSummary_Format_IsEmpty_WhenOnlySecondaryProfessionsAreKnown()
    {
        var skills = new List<ProfessionSkill> { new("Cooking", 1, 75, true) };

        Assert.Equal(string.Empty, ProfessionsSkillSummary.Format(skills));
    }

    [Fact]
    public void CharacterSyncRejectionCopy_DescribeNotLinked_NoRosterPulled_IsNeutral()
    {
        Assert.Equal(
            "Not linked to you",
            CharacterSyncRejectionCopy.DescribeNotLinked("Player-4619-00B33CCD", null, null, "user-1"));
    }

    [Fact]
    public void CharacterSyncRejectionCopy_DescribeNotLinked_NotLinkedToAnyone()
    {
        var characters = new List<DirectoryCharacter> { new("Player-4619-00B33CCD", "Hoobi", 60, 11, null) };

        Assert.Equal(
            "Not linked to a Discord account yet: ask an officer",
            CharacterSyncRejectionCopy.DescribeNotLinked("Player-4619-00B33CCD", characters, [], "user-1"));
    }

    [Fact]
    public void CharacterSyncRejectionCopy_DescribeNotLinked_LinkedToAnotherKnownPerson()
    {
        var characters = new List<DirectoryCharacter> { new("Player-4619-00B33CCD", "Hoobi", 60, 11, "user-2") };
        var people = new List<DirectoryPerson> { new("user-2", "Grug", null) };

        Assert.Equal(
            "Linked to Grug in the guild roster",
            CharacterSyncRejectionCopy.DescribeNotLinked("Player-4619-00B33CCD", characters, people, "user-1"));
    }

    [Fact]
    public void CharacterSyncRejectionCopy_DescribeNotLinked_LinkedToAnotherUnknownPerson()
    {
        var characters = new List<DirectoryCharacter> { new("Player-4619-00B33CCD", "Hoobi", 60, 11, "user-2") };

        Assert.Equal(
            "Linked to another Discord account",
            CharacterSyncRejectionCopy.DescribeNotLinked("Player-4619-00B33CCD", characters, [], "user-1"));
    }

    [Fact]
    public void CharacterSyncRejectionCopy_DescribeNotLinked_LinkedToTheSignedInUser()
    {
        var characters = new List<DirectoryCharacter> { new("Player-4619-00B33CCD", "Hoobi", 60, 11, "user-1") };

        Assert.Equal(
            "Linked to you",
            CharacterSyncRejectionCopy.DescribeNotLinked("Player-4619-00B33CCD", characters, [], "user-1"));
    }

    [Theory]
    [InlineData(1, "Warrior")]
    [InlineData(6, "Death Knight")]
    [InlineData(11, "Druid")]
    public void WowClasses_NameFor_KnownIds(int classId, string expected)
    {
        Assert.Equal(expected, WowClasses.NameFor(classId));
    }

    [Fact]
    public void WowClasses_NameFor_FallsBackForAnUnknownId()
    {
        Assert.Equal("Class 99", WowClasses.NameFor(99));
    }
}
