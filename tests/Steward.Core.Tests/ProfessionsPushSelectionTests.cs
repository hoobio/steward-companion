namespace Steward.Core.Tests;

public sealed class ProfessionsPushSelectionTests
{
    private const string HoobiGuid = "Player-4395-0A1B2C3D";
    private const string GrugGuid = "Player-4395-11111111";
    private const string MyUserId = "123";

    private static readonly CharacterObservation Hoobi = new(HoobiGuid, "Hoobi", "Nightslayer", "Gigagrug", 60, 1, 2, 1, null, null, false, null);
    private static readonly CharacterObservation Grug = new(GrugGuid, "Grug", "Nightslayer", "Gigagrug", 58, 7, 3, 2, null, null, false, null);

    private static CharacterProfessions Skills(int rank, long? observedAt = 1) =>
        new(observedAt, [new ProfessionSkill("Mining", rank, 300, false)], null, "fp");

    private static readonly Dictionary<string, CharacterProfessions> Professions = new()
    {
        [HoobiGuid] = Skills(150),
        [GrugGuid] = Skills(75),
    };

    private static readonly Dictionary<string, ProfessionCatalogue> Catalogue = new()
    {
        ["Mining"] = new ProfessionCatalogue(1, [new CatalogueRecipe("Smelt Copper", 2657, null, 2840, null, null)]),
    };

    private static CharacterPushRecord AllAccepted(IReadOnlyDictionary<string, CharacterProfessions> professions, string? catalogueFingerprint) => new(
        "batch",
        DateTimeOffset.UnixEpoch,
        2,
        Characters: professions.ToDictionary(
            p => p.Key,
            p => new CharacterPushOutcome(true, Fingerprint: CharacterSyncMapping.ProfessionsFingerprint(p.Value))),
        CatalogueFingerprint: catalogueFingerprint);

    private static ProfessionsPushPlan Select(
        IReadOnlyDictionary<string, CharacterProfessions> professions,
        CharacterPushRecord? last,
        IReadOnlyList<DirectoryCharacter>? roster = null,
        bool force = false) =>
        ProfessionsPushSelection.Select([Hoobi, Grug], professions, Catalogue, last, roster, MyUserId, force);

    [Fact]
    public void Select_SendsEverything_WhenThereIsNoPriorPush()
    {
        var plan = Select(Professions, null);

        Assert.Equal([HoobiGuid, GrugGuid], plan.Characters.Select(c => c.CharacterGuid));
        Assert.True(plan.SendCatalogue);
    }

    [Fact]
    public void Select_SendsNothing_WhenNothingChanged()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue));

        var plan = Select(Professions, last);

        Assert.Empty(plan.Characters);
        Assert.False(plan.SendCatalogue);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void Select_IgnoresObservedAtRestamps()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue));
        var restamped = new Dictionary<string, CharacterProfessions>
        {
            [HoobiGuid] = Skills(150, observedAt: 99),
            [GrugGuid] = Skills(75, observedAt: 99),
        };

        Assert.False(Select(restamped, last).HasWork);
    }

    [Fact]
    public void Select_SendsOnlyTheChangedCharacter()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue));
        var changed = new Dictionary<string, CharacterProfessions>(Professions) { [GrugGuid] = Skills(76) };

        var plan = Select(changed, last);

        Assert.Equal(GrugGuid, Assert.Single(plan.Characters).CharacterGuid);
        Assert.False(plan.SendCatalogue);
    }

    [Fact]
    public void Select_SendsOnlyTheCatalogue_WhenOnlyTheCatalogueChanged()
    {
        var last = AllAccepted(Professions, "stale");

        var plan = Select(Professions, last);

        Assert.Empty(plan.Characters);
        Assert.True(plan.SendCatalogue);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void Select_SkipsAPreviouslyRejectedCharacter_WhenItIsUnchanged()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue)) with
        {
            Characters = new Dictionary<string, CharacterPushOutcome>
            {
                [HoobiGuid] = new(true, Fingerprint: CharacterSyncMapping.ProfessionsFingerprint(Professions[HoobiGuid])),
                [GrugGuid] = new(false, CharacterSyncRejectionCopy.NotLinkedReason, CharacterSyncMapping.ProfessionsFingerprint(Professions[GrugGuid])),
            },
        };
        var roster = new List<DirectoryCharacter> { new(GrugGuid, "Grug", 58, 7, "someone-else") };

        Assert.False(Select(Professions, last, roster).HasWork);
    }

    [Fact]
    public void Select_ResendsAPreviouslyRejectedCharacter_WhenTheRosterNowLinksItToMe()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue)) with
        {
            Characters = new Dictionary<string, CharacterPushOutcome>
            {
                [HoobiGuid] = new(true, Fingerprint: CharacterSyncMapping.ProfessionsFingerprint(Professions[HoobiGuid])),
                [GrugGuid] = new(false, CharacterSyncRejectionCopy.NotLinkedReason, CharacterSyncMapping.ProfessionsFingerprint(Professions[GrugGuid])),
            },
        };
        var roster = new List<DirectoryCharacter> { new(GrugGuid, "Grug", 58, 7, MyUserId) };

        Assert.Equal(GrugGuid, Assert.Single(Select(Professions, last, roster).Characters).CharacterGuid);
    }

    [Fact]
    public void Select_ResendsAPreviouslyRejectedCharacter_WhenItsDataChanged()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue)) with
        {
            Characters = new Dictionary<string, CharacterPushOutcome>
            {
                [HoobiGuid] = new(true, Fingerprint: CharacterSyncMapping.ProfessionsFingerprint(Professions[HoobiGuid])),
                [GrugGuid] = new(false, "professions integrity check failed", CharacterSyncMapping.ProfessionsFingerprint(Professions[GrugGuid])),
            },
        };
        var changed = new Dictionary<string, CharacterProfessions>(Professions) { [GrugGuid] = Skills(80) };

        Assert.Equal(GrugGuid, Assert.Single(Select(changed, last).Characters).CharacterGuid);
    }

    [Fact]
    public void Select_SendsEverything_WhenForced()
    {
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue));

        var plan = Select(Professions, last, force: true);

        Assert.Equal(2, plan.Characters.Count);
        Assert.True(plan.SendCatalogue);
    }

    [Fact]
    public void Merge_RecordsSentOutcomesAndKeepsUnsentOnes()
    {
        var hoobiFingerprint = CharacterSyncMapping.ProfessionsFingerprint(Professions[HoobiGuid]);
        var previous = new Dictionary<string, CharacterPushOutcome> { [HoobiGuid] = new(true, Fingerprint: hoobiFingerprint) };
        var plan = Select(Professions, AllAccepted(Professions, null) with { Characters = previous });

        var merged = ProfessionsPushSelection.Merge(previous, plan, new Dictionary<string, string> { [GrugGuid] = "not linked to you" });

        Assert.Equal(previous[HoobiGuid], merged[HoobiGuid]);
        Assert.Equal(ProfessionsCharacterState.Rejected, ProfessionsPushSelection.StateOf(merged[GrugGuid], plan.Fingerprints[GrugGuid]));
    }

    [Fact]
    public void StateOf_IsSynced_WhenAcceptedAndUnchanged()
    {
        Assert.Equal(ProfessionsCharacterState.Synced, ProfessionsPushSelection.StateOf(new CharacterPushOutcome(true, Fingerprint: "fp"), "fp"));
    }

    [Fact]
    public void StateOf_IsPending_WhenAcceptedAndChanged()
    {
        Assert.Equal(ProfessionsCharacterState.Pending, ProfessionsPushSelection.StateOf(new CharacterPushOutcome(true, Fingerprint: "old"), "new"));
    }

    [Fact]
    public void StateOf_IsPending_WhenNeverSent()
    {
        Assert.Equal(ProfessionsCharacterState.Pending, ProfessionsPushSelection.StateOf(null, "fp"));
    }

    [Fact]
    public void StateOf_IsRejected_WhenRejectedAndUnchanged()
    {
        var outcome = new CharacterPushOutcome(false, CharacterSyncRejectionCopy.NotLinkedReason, "fp");

        Assert.Equal(ProfessionsCharacterState.Rejected, ProfessionsPushSelection.StateOf(outcome, "fp"));
    }

    [Fact]
    public void StateOf_StaysRejected_WhenRejectedAndChanged()
    {
        var outcome = new CharacterPushOutcome(false, CharacterSyncRejectionCopy.NotLinkedReason, "old");

        Assert.Equal(ProfessionsCharacterState.Rejected, ProfessionsPushSelection.StateOf(outcome, "new"));
    }

    [Fact]
    public void StateOf_StaysRejected_WhenTheRosterNowLinksItToMe_UntilAnAcceptedPush()
    {
        var hoobiFingerprint = CharacterSyncMapping.ProfessionsFingerprint(Professions[HoobiGuid]);
        var grugFingerprint = CharacterSyncMapping.ProfessionsFingerprint(Professions[GrugGuid]);
        var rejected = new CharacterPushOutcome(false, CharacterSyncRejectionCopy.NotLinkedReason, grugFingerprint);
        var last = AllAccepted(Professions, CharacterSyncMapping.CatalogueFingerprint(Catalogue)) with
        {
            Characters = new Dictionary<string, CharacterPushOutcome>
            {
                [HoobiGuid] = new(true, Fingerprint: hoobiFingerprint),
                [GrugGuid] = rejected,
            },
        };
        var plan = Select(Professions, last, [new DirectoryCharacter(GrugGuid, "Grug", 58, 7, MyUserId)]);

        Assert.Equal(ProfessionsCharacterState.Rejected, ProfessionsPushSelection.StateOf(rejected, grugFingerprint));

        var merged = ProfessionsPushSelection.Merge(last.Characters, plan, new Dictionary<string, string>());

        Assert.Equal(ProfessionsCharacterState.Synced, ProfessionsPushSelection.StateOf(merged[GrugGuid], grugFingerprint));
    }
}
