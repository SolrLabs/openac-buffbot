using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Tests.Donations;

/// <summary>The permanent donations list: round trip, restart-safe append, and the
/// never-overwrite corrupt-file rule.</summary>
public sealed class ContributorLedgerTests
{
    private const uint CharacterId = 999u;
    private const uint DonorId = 1234u;

    private static CompletedDonation Donation(string donorName, uint donorObjectId, DateTime utc, params DonatedItem[] items) =>
        new(donorName, donorObjectId, items, utc);

    [Fact]
    public void ACharacterNeverSeenBeforeLoadsAnEmptyList()
    {
        var ledger = new ContributorLedger(new FakeStorage(), warn: _ => { });

        Assert.Empty(ledger.Load(CharacterId));
    }

    [Fact]
    public void AnAppendedDonationRoundTripsThroughLoad()
    {
        var storage = new FakeStorage();
        var ledger = new ContributorLedger(storage, warn: _ => { });
        var utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        ledger.Append(CharacterId, Donation("Archer", DonorId, utc, new DonatedItem("Lead Scarab", 691u, 5)));

        Contributor contributor = Assert.Single(ledger.Load(CharacterId));
        Assert.Equal(DonorId, contributor.DonorObjectId);
        Assert.Equal("Archer", contributor.DonorName);
        ContributorEntry entry = Assert.Single(contributor.Entries);
        Assert.Equal("Lead Scarab", entry.ItemName);
        Assert.Equal(691u, entry.WeenieClassId);
        Assert.Equal(5, entry.Count);
        Assert.Equal(utc, entry.UtcTime);
    }

    [Fact]
    public void TwoAppendsAcrossTwoInstancesOverTheSameStorageBothPersist()
    {
        var storage = new FakeStorage();
        var utc1 = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var utc2 = utc1.AddMinutes(5);

        new ContributorLedger(storage, warn: _ => { })
            .Append(CharacterId, Donation("Archer", DonorId, utc1, new DonatedItem("Lead Scarab", 691u, 5)));

        // A fresh instance, the same shape a bot restart takes.
        new ContributorLedger(storage, warn: _ => { })
            .Append(CharacterId, Donation("Archer", DonorId, utc2, new DonatedItem("Iron Scarab", 689u, 2)));

        Contributor contributor = Assert.Single(new ContributorLedger(storage, warn: _ => { }).Load(CharacterId));
        Assert.Equal(2, contributor.Entries.Count);
    }

    [Fact]
    public void DifferentDonorsGroupSeparatelyAndSortByMostRecentDonationFirst()
    {
        var storage = new FakeStorage();
        var ledger = new ContributorLedger(storage, warn: _ => { });
        var earlier = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddHours(1);

        ledger.Append(CharacterId, Donation("Archer", 1u, earlier, new DonatedItem("Lead Scarab", 691u, 1)));
        ledger.Append(CharacterId, Donation("Mage", 2u, later, new DonatedItem("Iron Scarab", 689u, 1)));

        IReadOnlyList<Contributor> loaded = ledger.Load(CharacterId);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Mage", loaded[0].DonorName);
        Assert.Equal("Archer", loaded[1].DonorName);
    }

    [Fact]
    public void ACorruptPrimaryFileIsNeverOverwrittenAndAppendContinuesInARecoveredFile()
    {
        var storage = new FakeStorage();
        storage.Corrupt("contributors/999", "not json at all {{{");
        var warnings = new List<string>();
        var ledger = new ContributorLedger(storage, warn: warnings.Add);
        var utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        ledger.Append(CharacterId, Donation("Archer", DonorId, utc, new DonatedItem("Lead Scarab", 691u, 1)));

        Assert.Equal("not json at all {{{", storage.ReadText("contributors/999"));
        Assert.NotNull(storage.ReadText("contributors/999.recovered-1"));
        Assert.NotEmpty(warnings);
    }

    /// <summary>Load merges the corrupt primary's recovered sibling in, silently skipping the
    /// primary itself (already warned about).</summary>
    [Fact]
    public void LoadMergesACorruptPrimaryAndItsRecoveredFileTogether()
    {
        var storage = new FakeStorage();
        storage.Corrupt("contributors/999", "not json at all {{{");
        var ledger = new ContributorLedger(storage, warn: _ => { });
        var utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        ledger.Append(CharacterId, Donation("Archer", DonorId, utc, new DonatedItem("Lead Scarab", 691u, 1)));
        ledger.Append(CharacterId, Donation("Archer", DonorId, utc.AddMinutes(1), new DonatedItem("Iron Scarab", 689u, 1)));

        Contributor contributor = Assert.Single(ledger.Load(CharacterId));
        Assert.Equal(2, contributor.Entries.Count);
    }

    /// <summary>A second corrupt file (the recovered one, this time) is stepped past exactly the
    /// same way — neither is ever overwritten.</summary>
    [Fact]
    public void ASecondCorruptRecoveredFileIsAlsoLeftUntouched()
    {
        var storage = new FakeStorage();
        storage.Corrupt("contributors/999", "{{{");
        storage.Corrupt("contributors/999.recovered-1", "also not json");
        var warnings = new List<string>();
        var ledger = new ContributorLedger(storage, warn: warnings.Add);
        var utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        ledger.Append(CharacterId, Donation("Archer", DonorId, utc, new DonatedItem("Lead Scarab", 691u, 1)));

        Assert.Equal("{{{", storage.ReadText("contributors/999"));
        Assert.Equal("also not json", storage.ReadText("contributors/999.recovered-1"));
        Assert.NotNull(storage.ReadText("contributors/999.recovered-2"));
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void UnavailableStorageNeverThrowsOnLoadOrAppend()
    {
        var storage = new FakeStorage { IsAvailable = false };
        var ledger = new ContributorLedger(storage, warn: _ => { });
        var utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        ledger.Append(CharacterId, Donation("Archer", DonorId, utc, new DonatedItem("Lead Scarab", 691u, 1)));

        Assert.Empty(ledger.Load(CharacterId));
    }

    [Fact]
    public void EachCharacterHasItsOwnLedger()
    {
        var storage = new FakeStorage();
        var ledger = new ContributorLedger(storage, warn: _ => { });
        var utc = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        ledger.Append(999u, Donation("Archer", DonorId, utc, new DonatedItem("Lead Scarab", 691u, 1)));

        Assert.Single(ledger.Load(999u));
        Assert.Empty(ledger.Load(111u));
    }
}
