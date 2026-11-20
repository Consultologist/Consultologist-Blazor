# Task 2 report: JsonLogicShape

Implemented `V31/JsonLogicShape.cs` (ShapeFaultKind, ShapeFault, JsonLogicShape: MaxDepth, Check, IsPath, IsPosition, Paths) and `tests/V31Logic/JsonLogicShapeTests.cs` (every brief row, plus extras: missing literal-not-text, terms_subset failWith, count_pass min over int.MaxValue, IsPath/IsPosition words, Paths at depth 80).

TDD: RED: with the implementation moved aside, `dotnet test ... --filter FullyQualifiedName~V31Logic` failed to compile (CS0246/CS0103 ShapeFaultKind not found). GREEN: same command, Passed 148/148; `FullyQualifiedName~V31` Passed 1418/1418, no new warnings.
Order note: implementation was drafted before the tests; RED was then shown by removing it.

Choices where the brief was silent:
- Own `Describe` (A02-001 words, "a number" for any number); SourceMembers.Describe says "a whole number", which contradicts the brief's `var:5` row.
- Depth: each object/array level counts 1, root 1; fault when depth > 64; TooDeep once, that branch not descended.
- ComputedLiteral/LiteralNotText `Detail` = what the operand is; Text = operand name.
- Literal-operand checks run only when the operand count is right; with a wrong count all operands are walked as rules.
- `if` with even count >= 4 (in range) also gives IfWithoutElse.
- IsPosition: ASCII-digit segment; blank word = no non-whitespace Rune (empty is blank).
- Paths descends into every operand array of any operation (not var/missing), and object fields.
