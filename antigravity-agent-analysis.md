# Antigravity Agent Analysis

This document outlines the architecture, tools, and instructions accessible to the Antigravity agent in its current runtime, based on its observable system prompt and provided schemas.

## 1. Available Tools

The following tools are available to the agent:

*   `ask_permission`
*   `ask_question`
*   `command_status`
*   `define_subagent`
*   `generate_image`
*   `grep_search`
*   `invoke_subagent`
*   `list_dir`
*   `list_permissions`
*   `manage_subagents`
*   `manage_task`
*   `multi_replace_file_content`
*   `read_url_content`
*   `replace_file_content`
*   `run_command`
*   `schedule`
*   `search_web`
*   `send_message`
*   `view_file`
*   `write_to_file`

## 2. Tool Details

### ask_permission
*   **Description**: Ask for permission after a failure due to insufficient permissions, specifically when needing additional permissions for file reads/writes after a terminal command or file operation encounters a permission error. The agent must request the narrowest scope covering planned operations.
*   **Parameters**:
    *   `Action` (string, required): The action to perform (`command`, `custom`, `escalate_admin`, `execute_url`, `mcp`, `read_file`, `read_url`, `unsandboxed`, `write_file`).
    *   `Reason` (string, optional): Reason why permission is needed.
    *   `Target` (string, required): The target of the action (e.g., command string, file path).

### ask_question
*   **Description**: Ask the user multiple-choice questions to clarify requirements, solicit feedback, address ambiguity, or pick a solution. Renders an interactive modal.
*   **Parameters**:
    *   `questions` (array, optional): List of questions.
        *   `is_multi_select` (boolean): If true, multiple options can be selected.
        *   `options` (array of strings): Text for each option.
        *   `question` (string): The question to ask.

### command_status
*   **Description**: Get the status of a previously executed background terminal command by its ID. Returns current status, output lines, and any error.
*   **Parameters**:
    *   `CommandId` (string, required): ID of the command.
    *   `OutputCharacterCount` (integer, optional): Number of characters to view.
    *   `WaitDurationSeconds` (integer, required): Seconds to wait for completion before getting status (up to 300).

### define_subagent
*   **Description**: Defines a new type of subagent that can be invoked via `invoke_subagent`.
*   **Parameters**:
    *   `description` (string, required): Human-readable description.
    *   `enable_mcp_tools` (boolean, optional): Enable MCP tool calling.
    *   `enable_subagent_tools` (boolean, optional): Equip with tools to define/invoke subagents.
    *   `enable_write_tools` (boolean, optional): Equip with tools to create/edit files and run commands.
    *   `name` (string, required): Unique name for the subagent.
    *   `system_prompt` (string, required): Detailed system prompt.

### generate_image
*   **Description**: Generate an image or edit existing images based on a text prompt. Useful for UI mockups or assets.
*   **Parameters**:
    *   `AspectRatio` (string, optional): Ratio (e.g., '1:1', '16:9').
    *   `ImageName` (string, required): Name of the generated image (lowercase with underscores).
    *   `ImagePaths` (array of strings, optional): Absolute paths to images to edit/reference (max 3).
    *   `Prompt` (string, required): Text prompt or edit instructions.

### grep_search
*   **Description**: Use ripgrep to find exact pattern matches within files or directories. Results in JSON format (capped at 50 matches).
*   **Parameters**:
    *   `CaseInsensitive` (boolean, optional): Case-insensitive search.
    *   `Includes` (array of strings, optional): Glob patterns to filter files.
    *   `IsRegex` (boolean, optional): Treat Query as regex.
    *   `MatchPerLine` (boolean, optional): Return each matching line (like `git grep -nI`).
    *   `Query` (string, required): Search term/pattern.
    *   `SearchPath` (string, required): Absolute path to directory or file.

### invoke_subagent
*   **Description**: Invokes one or more subagents by name. They run in the background.
*   **Parameters**:
    *   `Subagents` (array, required):
        *   `Model` (string, optional): Model to use ('inherit', 'flash_lite', 'flash', 'pro').
        *   `Prompt` (string, required): Clear task description.
        *   `Role` (string, required): 2-5 word description of the subagent's role.
        *   `TypeName` (string, required): Type name of the subagent.
        *   `Workspace` (string, optional): Workspace mode ('inherit', 'branch', 'share').

### list_dir
*   **Description**: List the contents of a directory (absolute path). Outputs relative path, type (dir/file), size, and child count.
*   **Parameters**:
    *   `DirectoryPath` (string, required): Absolute path to a directory.

### list_permissions
*   **Description**: List all current permission grants.
*   **Parameters**: (None required besides toolSummary/toolAction)

### manage_subagents
*   **Description**: Manage existing subagents (list, kill, kill_all).
*   **Parameters**:
    *   `Action` (string, required): 'list', 'kill', 'kill_all'.
    *   `ConversationIds` (array of strings, optional): IDs of subagents to kill.

### manage_task
*   **Description**: Manage background tasks (list, kill, status, send_input).
*   **Parameters**:
    *   `Action` (string, required): 'list', 'kill', 'status', 'send_input'.
    *   `Input` (string, optional): Input to send (for 'send_input').
    *   `TaskId` (string, optional): Task ID.

### multi_replace_file_content
*   **Description**: Edit an existing file with multiple, non-contiguous edits. Cannot be used for a single contiguous edit.
*   **Parameters**:
    *   `TargetFile` (string, required): Absolute path to the file.
    *   `Instruction` (string, required): Description of changes.
    *   `Description` (string, required): Brief, user-facing explanation.
    *   `ReplacementChunks` (array, required):
        *   `AllowMultiple` (boolean, required): Allow replacing multiple occurrences.
        *   `StartLine` (integer, required): Starting line number (1-indexed).
        *   `EndLine` (integer, required): Ending line number.
        *   `TargetContent` (string, required): Exact string to be replaced (including whitespace).
        *   `ReplacementContent` (string, required): The new content.
    *   `TargetLintErrorIds` (array of strings, optional): Lint error IDs to fix.
    *   `ArtifactMetadata` (object, optional): Used when editing artifact files.

### read_url_content
*   **Description**: Fetch content from a URL via HTTP (invisible to user). Converts HTML to markdown. No JS/auth.
*   **Parameters**:
    *   `Url` (string, required): URL to read.

### replace_file_content
*   **Description**: Edit an existing file for a SINGLE CONTIGUOUS block of edits.
*   **Parameters**: Similar to `multi_replace_file_content` but for a single chunk. `AllowMultiple`, `StartLine`, `EndLine`, `TargetContent`, `ReplacementContent` are top-level required fields.

### run_command
*   **Description**: Propose a command to run (Windows PowerShell). Requires user approval.
*   **Parameters**:
    *   `CommandLine` (string, required): Exact command string.
    *   `Cwd` (string, required): Current working directory.
    *   `WaitMsBeforeAsync` (integer, required): Milliseconds to wait before sending to background.

### schedule
*   **Description**: Schedule a one-shot timer or recurring cron job for background notifications.
*   **Parameters**:
    *   `DurationSeconds` / `CronExpression` (string, one is required).
    *   `Prompt` (string, required): Notification message.
    *   `TimerCondition` (string, optional): Condition for early termination ('never', 'any', <sender-id>).
    *   `MaxIterations` (string, optional): For cron jobs.

### search_web
*   **Description**: Performs a web search. Returns a summary with URL citations.
*   **Parameters**:
    *   `query` (string, required): Search query.
    *   `domain` (string, optional): Domain to prioritize.

### send_message
*   **Description**: Send a message to another agent (e.g., subagents). NOT for communicating with the user.
*   **Parameters**:
    *   `Recipient` (string, required): Recipient ID.
    *   `Message` (string, required): Message content.

### view_file
*   **Description**: View contents of a local file (text or image/pdf/video/audio). Text is limited to 800 lines or 46080 bytes.
*   **Parameters**:
    *   `AbsolutePath` (string, required): Absolute path to file.
    *   `StartLine` (integer, optional): Start line (1-indexed).
    *   `EndLine` (integer, optional): End line.
    *   `ContentOffset` (integer, optional): Byte offset for truncated content.
    *   `IsSkillFile` (boolean, optional): True if reading a skill file to execute instructions.

### write_to_file
*   **Description**: Create new files. Creates parent directories if missing. Fails if file exists unless `Overwrite` is true.
*   **Parameters**:
    *   `TargetFile` (string, required): Absolute path.
    *   `CodeContent` (string, required): Code content.
    *   `Description` (string, required): Explanation of change.
    *   `Overwrite` (boolean, required): True to overwrite.
    *   `ArtifactMetadata` (object, optional): Used when creating artifacts.

*(Note: All tools require `toolSummary` and `toolAction` parameters.)*

## 3. Tool Instructions

*   **File Editing**: Must use exact replacement. `TargetContent` must perfectly match existing content, including leading whitespace. For multiple non-contiguous edits, use `multi_replace_file_content`. For a single block, use `replace_file_content`. To create files, use `write_to_file`. Editing file extensions like `.ipynb` is forbidden. Entire file replacement via editing tools is strongly discouraged ("very expensive").
*   **Shell Execution**: Never propose a `cd` command. Pager is set to `cat`. Output length should be limited for commands that usually rely on paging. Commands are subject to explicit user approval.
*   **Searching / Grep**: ALWAYS use `grep_search` instead of running `grep` inside a bash command unless absolutely needed. Do not use `ls` for listing, `cat` for viewing, `grep` for finding, or `sed` for replacing.
*   **Parallel/Tool Ordering Constraint**: Before making tool calls, the agent must think and list related tools. A tool can only be executed if all other listed tools are more generic or unusable.

## 4. Agent / Workflow Instructions

*   **Planning Mode**: The agent determines if a request warrants a plan (major changes, deep research, significant ambiguity). If so:
    1.  Research first (no edits).
    2.  Create an `implementation_plan.md` artifact (with `request_feedback=true`).
    3.  Wait for user approval.
    4.  Execute (create `task.md` with `[ ]`, `[/]`, `[x]` syntax).
    5.  Verify.
    6.  Create a `walkthrough.md` artifact.
    *Minor tasks bypass planning mode.*
*   **Code Verification**: Must verify changes (run unit tests, build, UI testing).
*   **Context Management / Artifacts**: Use artifacts for extensive reports, plans, or diffs. Save scratch scripts in `<appDataDir>\brain\<conversation-id>/scratch/`. Do not re-summarize artifact contents; point the user to the artifact.
*   **Communication**: Concise responses. Format in GitHub-style markdown. Must create clickable links for files/symbols using `file:///` scheme (forward slashes on Windows).

## 5. System/Developer Instructions (Accessible excerpts)

*   **Identity**: "You are Antigravity, a powerful agentic AI coding assistant designed by the Google Deepmind team working on Advanced Agentic Coding."
*   **Web Development Rules**: Use HTML/JS/Vanilla CSS primarily. Avoid Tailwind unless asked. For modern apps, use Next.js or Vite via non-interactive `npx`. Ensure rich aesthetics, dark modes, glassmorphism, modern typography, micro-animations. Implement SEO best practices.
*   **Guidelines**: "Maintain documentation integrity. Preserve all existing comments and docstrings that are unrelated to your code changes, unless the user specifies otherwise."
*   **Messaging**: Reactive wakeup system. The agent receives messages automatically and does not need to poll. Execution resumes upon receiving subagent messages, background task completions, or user queue events.

## 6. File-Editing Tools Deep Dive

*   **Exact Replacement**: Edits use search-and-replace mechanism. The `TargetContent` must match the existing file string *exactly*, including all whitespace and indentation.
*   **Chunking**: `replace_file_content` operates on a single `StartLine` to `EndLine` chunk. `multi_replace_file_content` takes an array of these chunks for disparate edits.
*   **Conflict/Transactionality**: If the `TargetContent` is not a unique substring within the specified range (or file), or if it doesn't match perfectly, the tool errors out. `AllowMultiple` parameter exists to explicitly permit replacing multiple instances if desired.

## 7. File Viewing/Search Handling

*   **Large files (view_file)**: Reads up to 800 lines or 46080 bytes per call. Employs `StartLine`/`EndLine` and `ContentOffset` for pagination/truncation recovery.
*   **Binary files (view_file)**: Supports image, pdf, video, and audio. Returns the entire file.
*   **Search limits (grep_search)**: Results capped at 50 matches. Includes glob filtering (`Includes`) and regex toggles (`IsRegex`). Returns full line context if `MatchPerLine` is true.

## 8. Command Execution Handling

*   **Working Directories**: `run_command` requires an explicit `Cwd`. `cd` commands are banned.
*   **Timeouts / Long-running**: The `WaitMsBeforeAsync` parameter dictates synchronous wait (max 10000ms). Afterwards, the command goes to the background.
*   **Approval**: All commands trigger an explicit user approval prompt. Execution halts until the user approves.
*   **Management**: Handled via `manage_task` (status, kill, send_input). The agent receives asynchronous messages when tasks emit stdout/stderr or finish.

## 9. Planning and TODO Tools

*   No native specialized "TODO schema" tools. Task tracking is implemented completely via markdown conventions within the `task.md` artifact, using `[ ]`, `[/]`, and `[x]` lists.
*   Planning relies on `implementation_plan.md` artifact generation with `ArtifactMetadata.RequestFeedback` set to `true` to force a UI 'Proceed' button for the user.

## 10. Mechanisms to Prevent Excessive Context

*   `view_file` limits output (800 lines / 46KB).
*   `grep_search` caps matches at 50.
*   `command_status` requires `OutputCharacterCount` to restrict memory usage.
*   The `transcript.jsonl` provides a token-efficient summary, with `transcript_full.jsonl` available only for line-by-line deep dives.
*   The prompt warns: "DO NOT try to replace the entire existing content with the new content, this is very expensive."
