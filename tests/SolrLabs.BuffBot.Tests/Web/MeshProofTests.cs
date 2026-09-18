using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>The heartbeat response's proof-of-key: a spoke only trusts commands
/// whose <c>X-BuffBot-Proof</c> verifies against the nonce it sent and the shared key.</summary>
public sealed class MeshProofTests
{
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void AProofComputedWithTheRightKeyAndNonceVerifies()
    {
        string nonce = MeshProof.GenerateNonce();
        string proof = MeshProof.Compute(Key, nonce);

        Assert.True(MeshProof.Verify(Key, nonce, proof));
    }

    [Fact]
    public void AProofComputedWithTheWrongKeyDoesNotVerify()
    {
        string nonce = MeshProof.GenerateNonce();
        string proof = MeshProof.Compute("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210", nonce);

        Assert.False(MeshProof.Verify(Key, nonce, proof));
    }

    [Fact]
    public void AProofForADifferentNonceDoesNotVerify()
    {
        string proof = MeshProof.Compute(Key, MeshProof.GenerateNonce());

        Assert.False(MeshProof.Verify(Key, MeshProof.GenerateNonce(), proof));
    }

    [Fact]
    public void GeneratedNoncesAreThirtyTwoLowercaseHexCharacters()
    {
        string nonce = MeshProof.GenerateNonce();

        Assert.Equal(32, nonce.Length);
        Assert.Matches("^[0-9a-f]{32}$", nonce);
    }

    /// <summary>An invalid key never verifies, whatever proof is presented against it.</summary>
    [Fact]
    public void VerifyIsFalseForAnInvalidKey() =>
        Assert.False(MeshProof.Verify("", MeshProof.GenerateNonce(), "whatever"));

    [Fact]
    public void ComputeThrowsForAnInvalidKey() =>
        Assert.Throws<ArgumentException>(() => MeshProof.Compute("", MeshProof.GenerateNonce()));
}
