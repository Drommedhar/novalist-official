using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// One thing to do before the book is finished.
///
/// Todo comments are anchored to a passage and belong to the scene they sit
/// in. "Read the whole thing aloud" belongs to no passage and no scene.
/// </summary>
public class ProjectTaskTests
{
    [Fact]
    public void TwoTasksMadeAtOnceAreTellableApart()
    {
        var first = new ProjectTask { Text = "Read it aloud" };
        var second = new ProjectTask { Text = "Read it aloud" };

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void ATaskStartsUndoneAndUnfiled()
    {
        var task = new ProjectTask();

        Assert.False(task.Done);
        Assert.Null(task.DoneAt);
        // Empty rather than a default list name: the loose pile is not a
        // checklist, and naming it would make it look like one.
        Assert.Equal(string.Empty, task.List);
        Assert.Equal(string.Empty, task.SceneId);
    }

    [Fact]
    public void ATaskCanBelongToAListAndToAScene()
    {
        var task = new ProjectTask
        {
            Text = "Check the dates",
            List = "Revision pass one",
            ChapterGuid = "c1",
            SceneId = "s1"
        };

        Assert.Equal("Revision pass one", task.List);
        Assert.Equal("s1", task.SceneId);
    }

    [Fact]
    public void AProjectStartsWithNothingToDo()
        => Assert.Empty(new ProjectMetadata().Tasks);
}
