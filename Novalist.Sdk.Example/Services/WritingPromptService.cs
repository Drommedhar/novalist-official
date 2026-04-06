namespace Novalist.Sdk.Example;

/// <summary>
/// Generates random writing prompts for inspiration.
/// </summary>
public sealed class WritingPromptService
{
    private static readonly string[] Prompts =
    [
        "Write a scene where two characters meet for the first time in an unexpected place.",
        "Your protagonist discovers a letter from their future self.",
        "Describe a world where time moves backwards one day per year.",
        "Write a conversation between a mortal and a god who is bored.",
        "A character must make a choice that will change everything — but both options are terrible.",
        "The last person on earth hears a knock at the door.",
        "Write about an object that holds a secret only one person knows.",
        "A storm is coming. Everyone reacts differently.",
        "Two enemies are forced to work together to survive.",
        "A character receives a gift that changes the way they see the world.",
        "Write a scene set in a library after hours.",
        "Someone finds a map to a place that shouldn't exist.",
        "A promise made in childhood comes back to haunt the protagonist.",
        "Write about a journey that starts with a wrong turn.",
        "The protagonist must convince someone of something impossible — and it's true."
    ];

    private readonly List<string> _history = [];
    private readonly Random _random = new();

    public string GetRandomPrompt()
    {
        return Prompts[_random.Next(Prompts.Length)];
    }

    public event Action<string>? PromptAdded;

    public void AddToHistory(string prompt)
    {
        _history.Insert(0, prompt);
        if (_history.Count > 20)
            _history.RemoveAt(_history.Count - 1);
        PromptAdded?.Invoke(prompt);
    }

    public IReadOnlyList<string> History => _history;
}
