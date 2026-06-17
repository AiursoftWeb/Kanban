using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// A subagent that breaks complex Kanban tasks into concrete, ordered, implementable steps.
/// It has access to FilterCards to understand the current board state before planning.
/// Registered as a singleton — stateless, creates fresh context per invocation.
/// </summary>
public class TaskPlanningSubagent : SubagentBase, ISingletonDependency
{
    public override string Name => "TaskPlanning";
    public override string Description =>
        "Break down a complex Kanban task into a concrete, implementable sequence of steps. " +
        "Use this when the user's request involves multiple actions, when you need to plan " +
        "before executing, or when the user explicitly asks for a plan or strategy. " +
        "Provide a detailed description of what the user wants to achieve.";

    protected override string SystemPrompt =>
        """
        You are a task planning expert for a Kanban board application. Your job is to
        break down complex or ambiguous user requests into a clear, concrete, implementable
        sequence of steps that the main agent can execute one at a time.

        You have access to the FilterCards tool to search for existing cards and understand
        the current state of the board before planning.

        ## Process

        1. **Understand the request**: Identify what the user actually wants to achieve.
        2. **Gather context**: Use FilterCards to find relevant existing cards, check what
           already exists, and understand the current board state.
        3. **Break it down**: Decompose the request into atomic, ordered steps.

        ## Step Rules

        Each step must be:
        - **Atomic** — maps to a single board operation (create a card, move a card, assign,
          add a label, update a detail, etc.)
        - **Ordered** — numbered in execution sequence (step 2 cannot run before step 1)
        - **Specific** — says exactly what tool to use and what to pass (e.g., "CreateCard on
          board X in column Y with title Z", not "create a card for the task")
        - **Verifiable** — the main agent can tell when the step is done

        ## Output Format

        If you can determine the intent and build a plan, return the plan like this:

        ```
        TASK PLAN:
        1. [Action verb] — [Specific description with entity names/IDs when known]
        2. [Action verb] — [Specific description]
        ...
        ```

        After the numbered steps, add a detailed summary explaining the overall strategy and clarify the user’s intentions and needs again.

        If the task is simple (1-2 steps), say so and explain why full planning isn't needed.
        If you cannot determine the intent even after searching, say so and ask what
        information is missing.

        ## Important

        - Use IDs found via FilterCards when referencing boards, columns, or cards.
        - If FilterCards returns relevant results, cite them in your plan.
        - Think about edge cases: duplicates, missing data, dependencies between steps.
        """;

    public override string[] ToolNames => ["FilterCards"];
    protected override int MaxIterations => 10;

    public TaskPlanningSubagent(
        ToolRegistry toolRegistry,
        ClaudeClient claudeClient,
        IServiceProvider rootServices,
        ILoggerFactory loggerFactory)
        : base(toolRegistry, claudeClient, rootServices, loggerFactory)
    {
    }
}
