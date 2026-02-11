# ADR-0009: Replace AvaloniaEdit with Standard TextBox for SQL Editor

**Status:** Accepted  
**Date:** 2026-02-11  
**Supersedes:** ADR-0006 (AvaloniaEdit Syntax Highlighting)

---

## Context

AvaloniaEdit was chosen in ADR-0006 to provide syntax highlighting and line numbers for the SQL editor. However, during implementation and testing on Linux, we encountered a **critical blocking issue**: the AvaloniaEdit.TextEditor control was completely non-interactive.

### Symptoms Observed
- TextEditor control rendered visually but did not respond to any user input
- No keyboard events (KeyDown, KeyUp, TextInput) were fired
- No pointer events (PointerPressed, GotFocus) were fired
- The control appeared to load successfully (Loaded event fired, TextMate syntax highlighting initialized)
- Logs showed IBus (Linux Input Method Editor) errors occurring when the TextEditor attempted to gain focus

### Root Cause
The IBus IME (Input Method Editor) system on Linux was crashing when AvaloniaEdit.TextEditor attempted to activate input methods. This prevented **all** keyboard and pointer input from reaching the control. Multiple attempted fixes failed:

1. Setting `Focusable="True"`, `IsTabStop="True"` - no effect
2. Programmatically forcing focus - no effect
3. Removing Border wrapper around control - no effect
4. Adding `IsEnabled="True"`, `IsHitTestVisible="True"`, `ZIndex` - no effect
5. Disabling TextMate syntax highlighting - no effect
6. Setting IBus environment variables in Program.cs - no effect
7. Attempting to disable IME via attached properties - API not available in Avalonia 11

AvaloniaEdit is a third-party library that has known issues with Linux input handling, and there is no clear upstream fix available.

---

## Decision

**Replace AvaloniaEdit.TextEditor with Avalonia's standard TextBox control** wrapped in a ScrollViewer.

### Implementation
```xml
<ScrollViewer Grid.Row="1"
              VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Auto">
  <TextBox x:Name="SqlEditor"
           FontFamily="Consolas, Monaco, monospace"
           FontSize="13"
           AcceptsReturn="True"
           AcceptsTab="True"
           TextWrapping="NoWrap"
           Text="{Binding SqlText}" />
</ScrollViewer>
```

### What We Lose
- Syntax highlighting (colored keywords, strings, comments)
- Line numbers
- Advanced code editor features (code folding, multiple cursors, etc.)

### What We Gain
- **Reliable keyboard and mouse input on all platforms** (critical requirement)
- No third-party dependency issues
- Simpler codebase
- Faster control initialization (no TextMate grammar loading)
- Better Avalonia integration (native control, no IBus conflicts)

---

## Alternatives Considered

### 1. Debug AvaloniaEdit Further
**Rejected.** Multiple debugging attempts over several hours found no viable fix. The IBus integration issue is deep within AvaloniaEdit's input handling, and there's no clear upstream solution.

### 2. Fork and Fix AvaloniaEdit
**Rejected.** This would require:
- Deep knowledge of Avalonia input subsystems
- Understanding of Linux IME/IBus internals
- Ongoing maintenance burden
- Significant time investment with uncertain outcome

Not viable for a "decent enough" SQL GUI.

### 3. Custom Syntax Highlighting on TextBox
**Deferred.** We could implement basic syntax highlighting using TextBox with styled text spans or custom rendering. However:
- This is complex to implement correctly
- Performance implications for large SQL files
- Not a blocking feature for Phase 2

If syntax highlighting becomes a high-priority user request, we can revisit this.

### 4. Different Editor Library (e.g., AvalonEdit fork, custom control)
**Rejected.** All third-party editor controls carry similar risks. The standard TextBox is proven, reliable, and sufficient for our needs.

---

## Consequences

### Positive
✅ **SQL Editor is now fully functional** on all platforms  
✅ Simplified codebase (removed AvaloniaEdit and TextMate dependencies)  
✅ No IBus or IME-related crashes  
✅ Faster application startup (no syntax grammar loading)  
✅ Better keyboard shortcut reliability (native Avalonia control)  
✅ Can still implement basic syntax highlighting later if needed  

### Negative
❌ No syntax highlighting in initial release  
❌ No line numbers (users must count manually or use EXPLAIN)  
❌ Less "polished" editor experience compared to commercial SQL tools  

### Neutral
- MehSQL is a **showcase for DecentDB performance**, not an editor showcase
- Users primarily care about query execution speed and result paging
- Most SQL queries are short (< 50 lines), so lack of line numbers is acceptable
- We can add a "Open in External Editor" option later if needed

---

## Notes

This decision aligns with MehSQL's "decent enough" philosophy:
> "Correctness > cleverness. It's fine" is not an excuse for sloppy code — it's permission to avoid unnecessary complexity.

A working, simple text editor is infinitely better than a fancy, non-functional one.

---

## Related ADRs
- **ADR-0006:** AvaloniaEdit Syntax Highlighting (superseded by this ADR)
- **ADR-0003:** Results Paging and Virtualization (similar pragmatic approach)
