---
title: Quickstart
description: Get up and running with the Ktory core and your first script in 5 minutes
sidebar:
  order: 2
---

Without installing any software, you can immediately visit the <a href="https://reader.ktory.ink/" target="_blank" rel="noopener noreferrer">Ktory Online Reader</a> to experience Ktory sample scripts. You can also use this platform to start writing your very first Ktory script right in the browser.

Below, this guide will quickly introduce how to bring Ktory into your own projects.

As we mentioned in the introduction: **A Ktory script is the screenplay, and your program is the stage director.** Now, let's write our first scene and drive it with minimal C# code.

## Writing Your First Script

Create a file named `prologue.ktr` on your computer and write the following content:

```ktory
Alice: You're awake? How do you feel?
  .expression(smile)

You blink, looking at the unfamiliar girl before you.

#choice
  * [Who are you...?]
    The girl shakes her head without answering.
  * [Look around first]
    You survey your surroundings; this seems to be an ancient stone tower.

Alice: Let's get out of here first.
```

Ktory is committed to making scriptwriting as intuitive as writing lines in a play:
- `Alice:` specifies the speaker, followed by their dialogue.
- `.expression(smile)`: This is a **decorator** attached to the dialogue. You can freely define custom decorators, or omit them entirely.
- Lines without a speaker are treated as **narration**.
- `#choice`: Opens a choice block. Options are listed with `*` and enclosed in brackets `[]`.
- Whichever option the player picks, the story naturally converges back to Alice's final line.

## Setting Up the Teleprompter

The Ktory core (`Ktory.Core`) acts like a meticulous, composed **teleprompter**—it manages branching, loops, and narrative session records. Your game or terminal acts as the **stage director**—every time the player presses Enter or clicks the dialogue box, the director sends an advance signal to the teleprompter, which hands you the next line or choice to present.

Open your terminal, create a simple console application, and reference the Ktory core:

```bash
dotnet new console -n KtoryDemo
cd KtoryDemo
dotnet add reference ../Ktory/src/Ktory.Core/Ktory.Core.csproj
```

> **Tip**: If you are developing with Unity, there is no need to manually reference the source code. Simply install it via the Unity Package Manager (UPM) by providing the git URL. For details, see the [Unity Integration Guide](/en/03-integration/01-unity-upm/).

Place the `prologue.ktr` file you just wrote into your `KtoryDemo` project directory.

## Bringing the Script to Life

Replace the contents of `Program.cs` in the project with the following:

```csharp
using System;
using System.IO;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

// 1. Read and parse the script file
var file = KtoryParser.Parse(File.ReadAllText("prologue.ktr"));

// 2. Create the sequencer (teleprompter)
var player = new KtorySequencer(file);

// Listen for dispatched tags: notify us when expressions, sound effects, or action cues appear in the script
player.OnTagsDispatched += tags =>
{
    foreach (var tag in tags)
    {
        Console.WriteLine($"  [Tag] {tag.Name}");
    }
};

// Start the script in English (the teleprompter automatically prepares the first beat upon start)
player.Start(requestedLocale: "en");

// 3. Drive the loop: step forward on Enter, enter a number when encountering choices
while (player.Status != ExecutionStatus.Completed)
{
    // Encountered a choice menu: wait for player input
    if (player.Status == ExecutionStatus.AwaitingChoice)
    {
        var menu = player.CurrentChoice!;
        Console.WriteLine("\n=== Make a choice ===");
        for (int i = 0; i < menu.Options.Count; i++)
        {
            var opt = menu.Options[i];
            Console.WriteLine($"{i + 1}. {opt.Label} {(opt.CanSelect ? "" : "(already selected)")}");
        }

        Console.Write("> ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int choice) &&
            choice >= 1 && choice <= menu.Options.Count &&
            menu.Options[choice - 1].CanSelect)
        {
            // Submit choice: the core automatically advances to the first beat of the chosen branch
            player.SubmitChoice(menu.Options[choice - 1].Id, menu.PresentationId);
        }
        continue;
    }

    // A normal beat (character dialogue or narration)
    var beat = player.CurrentPayload;
    if (beat is null) break;

    string speaker = string.IsNullOrEmpty(beat.Speaker) ? "Narration" : beat.Speaker;
    Console.WriteLine($"[{speaker}] {beat.Content}");

    // Wait for the player to press Enter, then advance to the next step
    Console.ReadLine();
    player.Step(beat.PresentationId);
}

Console.WriteLine("\nScript playback completed!");
```

Now, run the following in your terminal:

```bash
dotnet run
```

Press Enter, and watch as you advance your encounter with Alice step by step.

## Key Details to Keep in Mind

- **Beat**: A logical stopping point for dialogue or narration. Each time you press Enter or click the dialogue box, the core advances through one "beat".
- **The teleprompter doesn't care about typewriters**: The core is only responsible for telling the game engine "it's time to show this line now." Whether that line appears via a typewriter letter-by-letter animation, fades in, or plays alongside voice acting is entirely up to the game engine.
- **Selecting is advancing**: When calling `SubmitChoice()` to submit an option, the core already turns to the first line of the selected branch and pauses there. You do not need to—and should not—call `Step()` an extra time.
- **Safety key against accidental clicks (`PresentationId`)**: You may have noticed `player.Step(beat.PresentationId)`. This ID acts like a temporary ticket that the core uses to verify "the line you are confirming is indeed the line currently being presented," preventing rapid clicks or accidental skips from advancing past subsequent narrative beats uncontrollably.

## Next Steps

Now you have mastered Ktory's core interaction loop! From here, you can:

- Head over to [Script Syntax](/en/02-syntax/01-anchor-decorator/) to unlock loops, conditions, and custom decorators.
- Explore [Localization & Multilingual Support](/en/02-syntax/04-localization/) to experience the convenience of seamless hot-reloading across languages without duplicating script files.
- Check out the [Unity Integration Guide](/en/03-integration/01-unity-upm/) to connect the teleprompter into real game visuals and staging.
