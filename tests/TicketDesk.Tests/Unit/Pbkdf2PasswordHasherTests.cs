using TicketDesk.Api.Security;
using Xunit;

namespace TicketDesk.Tests.Unit;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var (hash, salt) = _hasher.Hash("ClaveSegura123!");

        Assert.True(_hasher.Verify("ClaveSegura123!", hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForIncorrectPassword()
    {
        var (hash, salt) = _hasher.Hash("ClaveSegura123!");

        Assert.False(_hasher.Verify("OtraClave456!", hash, salt));
    }

    [Fact]
    public void Hash_GeneratesDifferentSalts_ForSamePassword()
    {
        var first = _hasher.Hash("MismaClave123!");
        var second = _hasher.Hash("MismaClave123!");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }
}
