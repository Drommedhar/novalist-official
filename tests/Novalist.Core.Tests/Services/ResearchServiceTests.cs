using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ResearchServiceTests
{
    private static (ResearchService Sut, IProjectService Project) Build(string? root = null)
    {
        var project = Substitute.For<IProjectService>();
        if (root != null) project.ProjectRoot.Returns(root);
        return (new ResearchService(project, new FileService()), project);
    }

    [Fact]
    public void GetAll_NoProject_ReturnsEmpty()
    {
        var (sut, project) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        Assert.Empty(sut.GetAll());
    }

    [Fact]
    public void GetAll_OrdersByOrderThenCreatedAt()
    {
        var (sut, project) = Build();
        var meta = new ProjectMetadata
        {
            ResearchItems =
            {
                new ResearchItem { Id = "b", Order = 1 },
                new ResearchItem { Id = "a", Order = 0 }
            }
        };
        project.CurrentProject.Returns(meta);
        var all = sut.GetAll();
        Assert.Equal("a", all[0].Id);
    }

    [Fact]
    public async Task SaveAsync_NoProject_NoOp()
    {
        var (sut, project) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        await sut.SaveAsync(new ResearchItem());
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task SaveAsync_AddsNew_SetsOrderAndTimestamp()
    {
        var (sut, project) = Build();
        var meta = new ProjectMetadata { ResearchItems = { new ResearchItem { Id = "x" } } };
        project.CurrentProject.Returns(meta);
        var item = new ResearchItem { Id = "new" };

        await sut.SaveAsync(item);

        Assert.Equal(2, meta.ResearchItems.Count);
        Assert.Equal(1, item.Order);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task SaveAsync_UpdatesExisting()
    {
        var (sut, project) = Build();
        var meta = new ProjectMetadata { ResearchItems = { new ResearchItem { Id = "x", Title = "old" } } };
        project.CurrentProject.Returns(meta);

        await sut.SaveAsync(new ResearchItem { Id = "x", Title = "new" });

        Assert.Single(meta.ResearchItems);
        Assert.Equal("new", meta.ResearchItems[0].Title);
    }

    [Fact]
    public async Task DeleteAsync_NoProject_NoOp()
    {
        var (sut, project) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        await sut.DeleteAsync("x");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteAsync_Removes()
    {
        var (sut, project) = Build();
        var meta = new ProjectMetadata { ResearchItems = { new ResearchItem { Id = "x" } } };
        project.CurrentProject.Returns(meta);
        await sut.DeleteAsync("x");
        Assert.Empty(meta.ResearchItems);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task ImportFileAsync_NoProject_Throws()
    {
        var (sut, project) = Build();
        project.ProjectRoot.Returns((string?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ImportFileAsync("/nope.txt"));
    }

    [Fact]
    public async Task ImportFileAsync_CopiesIntoResearchFolder()
    {
        using var dir = new TempDir();
        var (sut, _) = Build(dir.Path);
        var src = Path.Combine(dir.Path, "source.txt");
        await File.WriteAllTextAsync(src, "data");

        var rel = await sut.ImportFileAsync(src);

        Assert.Equal("Research/source.txt", rel);
        Assert.True(File.Exists(Path.Combine(dir.Path, "Research", "source.txt")));
    }

    [Fact]
    public async Task ImportFileAsync_AppendsSuffixOnClash()
    {
        using var dir = new TempDir();
        var (sut, _) = Build(dir.Path);
        var src = Path.Combine(dir.Path, "source.txt");
        await File.WriteAllTextAsync(src, "data");

        var first = await sut.ImportFileAsync(src);
        var second = await sut.ImportFileAsync(src);

        Assert.Equal("Research/source.txt", first);
        Assert.Equal("Research/source (1).txt", second);
    }

    [Fact]
    public void GetAbsolutePath_NoRoot_ReturnsEmpty()
    {
        var (sut, project) = Build();
        project.ProjectRoot.Returns((string?)null);
        Assert.Equal(string.Empty, sut.GetAbsolutePath("Research/x.txt"));
    }

    [Fact]
    public void GetAbsolutePath_EmptyRelative_ReturnsEmpty()
    {
        var (sut, _) = Build(root: "/proj");
        Assert.Equal(string.Empty, sut.GetAbsolutePath(""));
    }

    [Fact]
    public void GetAbsolutePath_CombinesWithRoot()
    {
        var (sut, _) = Build(root: "/proj");
        var result = sut.GetAbsolutePath("Research/x.txt");
        Assert.Contains("Research", result);
        Assert.Contains("x.txt", result);
    }
}
