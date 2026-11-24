using NewCosmos.Services.Core;

namespace NewCosmos.Core.Tests;

public class CurrentUserContextTests
{
    [Fact]
    public void Begin_ScopesSnapshot_And_RestoresPrevious()
    {
        var first = new CurrentUserSnapshot(1, Guid.NewGuid(), "u1", "User One", "13800000001", 10);
        var second = new CurrentUserSnapshot(2, Guid.NewGuid(), "u2", "User Two", "13800000002", 20);
        var original = CurrentUserContext.Current;

        using (CurrentUserContext.Begin(first))
        {
            Assert.Equal(1, CurrentUserContext.UserId);
            Assert.Equal("u1", CurrentUserContext.UserName);

            using (CurrentUserContext.Begin(second))
            {
                Assert.Equal(2, CurrentUserContext.UserId);
                Assert.Equal("User Two", CurrentUserContext.FullName);
            }

            Assert.Equal(1, CurrentUserContext.UserId);
        }

        Assert.Same(original, CurrentUserContext.Current);
    }

    [Fact]
    public void DefaultSnapshot_IsUsed_WhenNoAmbientScope()
    {
        CurrentUserContext.SetDefault(new CurrentUserSnapshot(9, null, "desk", "Desktop User", "", 99));
        try
        {
            Assert.Equal(9, CurrentUserContext.UserId);
            Assert.Equal(99, CurrentUserContext.OrganizationId);
        }
        finally
        {
            CurrentUserContext.ClearDefault();
        }
    }
}
