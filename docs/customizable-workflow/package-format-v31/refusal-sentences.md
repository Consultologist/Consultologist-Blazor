# Refusal, warning and job-start sentences (specVersion 31)

Every sentence the v31 design gives to a refusal, a warning, a job-start check or a History reason, in one place. **Milestone 39 uses them as written,** with no manual review, so the build is never held up by wording; the conformance suite pins each one. **They are reviewed in #1483 (Milestone 40),** concurrently with the build ([decided](https://github.com/Consultologist/Consultologist-Blazor/issues/1388#issuecomment-6985257872)).

Every sentence follows the seven style rules in [index § 7](index.md#7-refusal-sentences). `<…>` marks a placeholder. **Audience** is *author* (the Editor, publish, import, CI) or *clinician* (the Create page, job start, History), where rule 7 applies. The sentences in the parts are the source: if this table and a part disagree, the part wins and this table is corrected.

| Part | Sentences |
|---|---|
| [01 Files and scopes](#01-files-and-scopes) | 42 |
| [02 Types and inputs](#02-types-and-inputs) | 56 |
| [03 Expressions](#03-expressions) | 44 |
| [04 Loops and functions](#04-loops-and-functions) | 80 |
| [05 Templates and macros](#05-templates-and-macros) | 64 |
| [06 Checks, deliverables and the profile](#06-checks-deliverables-and-the-profile) | 38 |
| [07 The compiled documents](#07-the-compiled-documents) | 17 |
| [08 The record](#08-the-record) | 9 |
| [09 The engine and the pages](#09-the-engine-and-the-pages) | 16 |
| [10 Migration and tools](#10-migration-and-tools) | 6 |
| **Total** | **372** |

## 01 Files and scopes

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S01-001 | [5. Members: every one written](01-files-and-scopes.md#5-members-every-one-written) | *"Did you mean 'label'?"* | author | |
| S01-002 | [Refusals](01-files-and-scopes.md#refusals) | *"Node '\<id\>' in \<path\> reuses the id of node '\<id\>' \<at the top level \| in \<other path\>\>, which \<path\> can read: rename one. Ids may repeat only where neither can see the other (for example in two sibling loops)."* | author | |
| S01-003 | [Refusals](01-files-and-scopes.md#refusals) | *"While '\<id\>' declares max \<n\>; a while runs at most FormatBounds.passes (15) passes."* | author | |
| S01-004 | [Refusals](01-files-and-scopes.md#refusals) | *"Call '\<id\>' reaches depth \<n\>; calls nest at most FormatBounds.depth (15) deep."* | author | |
| S01-005 | [Refusals](01-files-and-scopes.md#refusals) | *"Loop '\<id\>' declares maxParallel \<n\>; the most is FormatBounds.parallel (15)."* | author | |
| S01-006 | [Refusals](01-files-and-scopes.md#refusals) | *"File '\<path\>' is not listed: every file in \<folder\> is named once in its index.json, or is index.json or README.md."* | author | |
| S01-007 | [Refusals](01-files-and-scopes.md#refusals) | *"\<folder\> has no index.json; every folder lists its members, even when it has none (\"items\": [])."* | author | |
| S01-008 | [Refusals](01-files-and-scopes.md#refusals) | *"The package has no \<folder\>/ folder; all nine top-level folders are always present."* | author | |
| S01-009 | [Refusals](01-files-and-scopes.md#refusals) | *"\<list\> lists '\<id\>', but \<folder\> has no \<id\>.md or \<id\>/."* | author | |
| S01-010 | [Refusals](01-files-and-scopes.md#refusals) | *"\<list\> lists '\<id\>' twice."* | author | |
| S01-011 | [Refusals](01-files-and-scopes.md#refusals) | *"\<list\> lists '\<id\>/'; list a folder by its bare id."* | author | |
| S01-012 | [Refusals](01-files-and-scopes.md#refusals) | *"\<folder\> holds both \<id\>.md and \<id\>/: an id names one file or one folder."* | author | |
| S01-013 | [Refusals](01-files-and-scopes.md#refusals) | *"Delivery folder '\<path\>' is inside another delivery folder; delivery folders are one level deep."* | author | |
| S01-014 | [Refusals](01-files-and-scopes.md#refusals) | *"File '\<path\>' starts with a byte-order mark; save it as UTF-8 without one."* | author | |
| S01-015 | [Refusals](01-files-and-scopes.md#refusals) | *"File '\<path\>' contains a carriage return on line \<n\> that doesn't end the line; lines end with \n."* | author | |
| S01-016 | [Refusals](01-files-and-scopes.md#refusals) | *"File '\<path\>' must start with a line of exactly ---, then one JSON object, then a line of exactly ---."* | author | |
| S01-017 | [Refusals](01-files-and-scopes.md#refusals) | *"The header of '\<path\>' is not valid JSON: \<parser message\>."* | author | |
| S01-018 | [Refusals](01-files-and-scopes.md#refusals) | *"The header of '\<path\>' declares '\<member\>' twice."* | author | |
| S01-019 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Kind\> '\<id\>' has an empty body; its \<prompt \| template \| text\> is the body."* | author | |
| S01-020 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Kind\> '\<id\>' has a body; a \<computed node \| call \| check\> keeps everything in its header."* | author | |
| S01-021 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Declaration\> is missing '\<member\>'; every member is written, and null means none."* | author | |
| S01-022 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Declaration\> sets '\<member\>' to null, which it can never be."* | author | |
| S01-023 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Declaration\> in '\<path\>' declares unknown member '\<member\>'. Did you mean '\<closest\>'?"* | author | |
| S01-024 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Declaration\> has an empty description; write null for none."* | author | |
| S01-025 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Declaration\>'s description is \<n\> characters; the most is 500."* | author | |
| S01-026 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Declaration\> has an empty label."* | author | |
| S01-027 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Node\> '\<key\>' declares reproducible \<value\>, but its \<kind \| body \| function\> makes it \<derived\>."* | author | |
| S01-028 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Node\> '\<key\>' is kind \<kind\> and declares producer '\<value\>'; a \<kind\> node's producer is \<generated \| computed\>."* | author | |
| S01-029 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Node\> '\<key\>' declares \<modelDerived \| runDependent \| profileCarrying\> \<value\>, but its reads make it \<derived\>: \<the read that does, or none\>."* | author | |
| S01-030 | [Refusals](01-files-and-scopes.md#refusals) | *"'\<id\>' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores."* | author | |
| S01-031 | [Refusals](01-files-and-scopes.md#refusals) | *"Id '\<id\>' is \<n\> characters; the most is SourceBounds.idLength (64)."* | author | |
| S01-032 | [Refusals](01-files-and-scopes.md#refusals) | *"Id '\<id\>' is reserved on Windows (con, prn, aux, nul, com1–com9, lpt1–lpt9) and can't name a file: rename it."* | author | |
| S01-033 | [Refusals](01-files-and-scopes.md#refusals) | *"Id '\<id\>' is reserved: \<root \| member\> '\<word\>' is read in references, so nothing may be called '\<id\>'."* | author | |
| S01-034 | [Refusals](01-files-and-scopes.md#refusals) | *"Format '\<id\>' reuses the name of a \<built-in \| catalogue\> format: rename it."* | author | |
| S01-035 | [Refusals](01-files-and-scopes.md#refusals) | *"Renderer '\<id\>' reuses the name of a built-in renderer: rename it."* | author | |
| S01-036 | [Refusals](01-files-and-scopes.md#refusals) | *"Node '\<id\>' appears twice inside while '\<while\>' (\<path\> and \<path\>): pass.….node.\<id\> must name one node."* | author | |
| S01-037 | [Refusals](01-files-and-scopes.md#refusals) | *"The package has \<n\> files; the most is SourceBounds.files (512)."* | author | |
| S01-038 | [Refusals](01-files-and-scopes.md#refusals) | *"File '\<path\>' is \<n\> KiB; the most is SourceBounds.fileBytes (256 KiB)."* | author | |
| S01-039 | [Refusals](01-files-and-scopes.md#refusals) | *"The package is \<n\> MiB; the most is SourceBounds.totalBytes (2 MiB)."* | author | |
| S01-040 | [Refusals](01-files-and-scopes.md#refusals) | *"Path '\<path\>' is \<n\> characters; the most is SourceBounds.pathLength (200), counted from the package root."* | author | |
| S01-041 | [Refusals](01-files-and-scopes.md#refusals) | *"File '\<path\>' nests JSON more than 32 levels deep at \<JSON pointer\>; the most is SourceBounds.jsonDepth (32)."* | author | |
| S01-042 | [Refusals](01-files-and-scopes.md#refusals) | *"\<Site\> in '\<path\>' has \<n\> expression nodes; the most is SourceBounds.expressionNodes (1,000)."* | author | |
## 02 Types and inputs

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S02-001 | [1.2 The contagious flags: run-dependent and model-derived](02-types-and-inputs.md#12-the-contagious-flags-run-dependent-and-model-derived) | *"computed from model text: node.draft"* | author | |
| S02-002 | [2.1 Reading a value](02-types-and-inputs.md#21-reading-a-value) | *"Medical oncology"* | author | |
| S02-003 | [3.5 What a model receives](02-types-and-inputs.md#35-what-a-model-receives) | *"Not sent to the model: …"* | author | |
| S02-004 | [8.4 An example: `example-conditional-sections`' inputs at 31](02-types-and-inputs.md#84-an-example-example-conditional-sections-inputs-at-31) | *"Narrative"* | author | |
| S02-005 | [12. `WorkflowPackages/Current`: what satellites see](02-types-and-inputs.md#12-workflowpackagescurrent-what-satellites-see) | *"This workflow needs the web app"* | clinician | |
| S02-006 | [Refusals](02-types-and-inputs.md#refusals) | *"computed from model text: \<key\>"* | author | |
| S02-007 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' declares unknown kind '\<kind\>' (accepted: agent, rendered, computed, for, while, if, case, call)."* | author | |
| S02-008 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' declares kind let; a let is now kind computed, with its JSON Logic in expression."* | author | |
| S02-009 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' is kind \<kind\> and declares producer '\<value\>'; a \<kind\> node's producer is \<generated \| computed\>."* | author | |
| S02-010 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' declares producer '\<value\>', which is reserved for a later version (accepted at 31: generated, computed)."* | author | |
| S02-011 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' declares \<modelDerived \| runDependent \| profileCarrying\> \<value\>, but its reads make it \<derived\>: \<the read that does, or none\>."* | author | |
| S02-012 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' is kind agent and returns \<format\>, which has no binding for producer \<producer\> in agents/bindings.json."* | author | |
| S02-013 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' is kind rendered and returns \<format\>; a rendered node returns plaintext, markdown_block or markdown_inline."* | author | |
| S02-014 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' is kind agent and declares an inline format; an agent's format is named, in formats/."* | author | |
| S02-015 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Flow\> '\<id\>' declares format \<declared\>, but its ret gives \<derived\>."* | author | |
| S02-016 | [Refusals](02-types-and-inputs.md#refusals) | *"Branch \<n\> of \<if \| case\> '\<id\>' returns \<format\>, which is not in the declared union \<union\>."* | author | |
| S02-017 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' is not a built-in, catalogue or package format."* | author | |
| S02-018 | [Refusals](02-types-and-inputs.md#refusals) | *"'input.\<id\>' is read bare; read input.\<id\>.value, or input.\<id\>.content for text."* | author | |
| S02-019 | [Refusals](02-types-and-inputs.md#refusals) | *"'\<path\>.title' reads the title of a plain enum, which has none; use a labelled oneOf, or read .value."* | author | |
| S02-020 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Condition\> compares \<path\>.title with '\<literal\>', which is not one of its titles (\<titles\>)."* | author | |
| S02-021 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format \| Input '\<id\>'\> uses keyword '\<keyword\>' at \<path\>, which is outside the formats dialect."* | author | |
| S02-022 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' declares no $schema; every format file names the dialect (urn:consultologist:spec:31:dialect)."* | author | |
| S02-023 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' declares $schema '\<value\>', but the package is specVersion \<n\>, whose dialect is \<id\>."* | author | |
| S02-024 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> declares additionalProperties \<value\>; every object declares additionalProperties: false."* | author | |
| S02-025 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> lists '\<property\>' in required but declares no such property."* | author | |
| S02-026 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> leaves '\<property\>' out of required; every property is listed in required, and an optional one is written as nullable."* | author | |
| S02-027 | [Refusals](02-types-and-inputs.md#refusals) | *"Node '\<id\>' returns \<format\>, an anyOf at its root, which strict mode can't send to a model; wrap it in an object."* | author | |
| S02-028 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> writes a nullable \<type\> with anyOf; write it as \"type\": [\"\<type\>\", \"null\"]."* | author | |
| S02-029 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> declares type \<list\>; a type array is one type plus \"null\"."* | author | |
| S02-030 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> uses format '\<value\>'; the only format is date, on a string."* | author | |
| S02-031 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> has '\<keyword\>' beside $ref; only title and description may sit beside a $ref."* | author | |
| S02-032 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> at \<path\> refers to itself; recursive formats aren't supported."* | author | |
| S02-033 | [Refusals](02-types-and-inputs.md#refusals) | *"Package format '\<id\>' declares $id; a package format's $id is written at publish."* | author | |
| S02-034 | [Refusals](02-types-and-inputs.md#refusals) | *"Package format '\<id\>' is a \<string \| number \| boolean\>; a package defines object and array formats, and refers to built-in and catalogue formats by name."* | author | |
| S02-035 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' is a text format and declares no contentMediaType."* | author | |
| S02-036 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' is Markdown and declares no x-markdown (block or inline)."* | author | |
| S02-037 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Format\> declares \<contentMediaType \| x-markdown\> at \<path\>, where it doesn't apply."* | author | |
| S02-038 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Input '\<id\>' \| Node '\<id\>'\>'s inline format declares \<contentMediaType \| x-markdown\>; text inside an inline format is a $ref to a text format."* | author | |
| S02-039 | [Refusals](02-types-and-inputs.md#refusals) | *"formats/index.json lists '\<id\>', but formats/ has no \<id\>.json."* | author | |
| S02-040 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' takes markdown_inline; use plaintext or markdown_block."* | author | |
| S02-041 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' takes a choice of shapes (anyOf); an input has one shape."* | author | |
| S02-042 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' declares channel '\<channel\>' (accepted: transcript, image, ambient-note, form)."* | author | |
| S02-043 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' declares channel '\<channel\>', which is only for a text input or an array of text."* | author | |
| S02-044 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' declares channel 'form', which needs an object with at least one property."* | author | |
| S02-045 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' field '\<property\>' has no title; the Create page labels each field by its title."* | author | |
| S02-046 | [Refusals](02-types-and-inputs.md#refusals) | *"Input '\<id\>' declares default; inputs have no default at 31."* | author | |
| S02-047 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> declares one value; an enum with one value is a constant, not a choice."* | author | |
| S02-048 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> declares value '\<value\>' twice."* | author | |
| S02-049 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> value '\<value\>' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores)."* | author | |
| S02-050 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> gives two values the title '\<title\>'."* | author | |
| S02-051 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> writes oneOf branch \<n\> as \<shape\>; each branch is exactly { \"const\": \<value\>, \"title\": \<label\> }."* | author | |
| S02-052 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> declares both enum and oneOf; write one."* | author | |
| S02-053 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> is nullable but its enum doesn't list null; a nullable enum lists null among its values."* | author | |
| S02-054 | [Refusals](02-types-and-inputs.md#refusals) | *"\<Subject\> is nullable but its oneOf has no { "const": null } arm; a nullable labelled enum adds one."* | author | |
| S02-055 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' declares \<n\> properties; a format sent to a model declares at most \<limit\>."* | author | |
| S02-056 | [Refusals](02-types-and-inputs.md#refusals) | *"Format '\<name\>' nests \<n\> levels deep at \<path\>; a format sent to a model nests at most \<limit\>."* | author | |
## 03 Expressions

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S03-001 | [8. The printer](03-expressions.md#8-the-printer) | *"skipped: input.kind.value = "table" is false"* | clinician | |
| S03-002 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> compares \<path\> (\<format\>) with a literal; text is tested by whether it is blank, its size, or in."* | author | |
| S03-003 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> compares \<path\> (\<format\>) with \<path\> (\<format\>); text is tested by whether it is blank, its size, or in."* | author | |
| S03-004 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> uses ==; equality is written === (shown as =)."* | author | |
| S03-005 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> uses !=; inequality is written !== (shown as ≠)."* | author | |
| S03-006 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> uses \<operation\>, which is not a JSON Logic operation of this format (\<the allowed list\>)."* | author | |
| S03-007 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> uses substr; text is cut with trim or plain, and measured with size."* | author | |
| S03-008 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> orders \<path\> (\<format\>); \<, \>, ≤ and ≥ compare numbers or dates."* | author | |
| S03-009 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> tests \<path\> (\<format\>) with not; not takes a boolean — test text with blank."* | author | |
| S03-010 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> gives \<operation\> \<n\> operands; it takes \<m\>."* | author | |
| S03-011 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> writes \<operation\> with a bare operand; operands are written as an array."* | author | |
| S03-012 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads var with a default; every read is one path."* | author | |
| S03-013 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads \<path\>, a position in a list; lists are read whole, or by identity through .items."* | author | |
| S03-014 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads \<path\>, which doesn't exist here."* | author | |
| S03-015 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads input.\<id\>; an input is read as input.\<id\>.value, .content or .title."* | author | |
| S03-016 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads \<path\>.title, but \<format\> is a plain enum with no titles; read .value, or give the format oneOf with const and title."* | author | |
| S03-017 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> compares \<path\>.title with "\<text\>", which is not one of its titles (\<titles\>)."* | author | |
| S03-018 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads profile.\<field\>; profile fields are read only in deliverable templates and macro bodies, never in conditions or computed expressions."* | author | |
| S03-019 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads \<root\>.\<rest\>; JSON Logic reads input, node, check, item, pass, param and run."* | author | |
| S03-020 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Deliverable '\<id\>' if reads node.\<id\>.content; a deliverable's if reads data (.value), not text."* | author | |
| S03-021 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Deliverable '\<id\>' if reads check.\<id\>; a deliverable's existence can't depend on a check."* | author | |
| S03-022 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Macro '\<id\>' if reads param.\<name\>; a macro's if is shared, so it can't depend on one function's parameter."* | author | |
| S03-023 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> compares \<path\> with "\<value\>", which is not one of its values (\<values\>)."* | author | |
| S03-024 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads \<path\>.value.\<field\>, which not every member of \<format\> declares."* | author | |
| S03-025 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> \<member\> yields \<format\>; a condition yields a boolean."* | author | |
| S03-026 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Computed node '\<id\>' yields \<format\>; it declares \<format\>."* | author | |
| S03-027 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Computed node '\<id\>' builds \<format\> without \<field\>; object must give every property of the format."* | author | |
| S03-028 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Computed node '\<id\>' builds \<format\> with \<field\>, which the format doesn't declare."* | author | |
| S03-029 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> gives \<operation\> a computed \<operand\>; \<operand\> must be written as a literal."* | author | |
| S03-030 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> gives count_pass a min of \<n\>; min is a whole number, 0 or more."* | author | |
| S03-031 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> pattern "\<pattern\>" is not I-Regexp (RFC 9485): \<reason\>."* | author | |
| S03-032 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> pattern "\<pattern\>" is \<n\> characters long; the most is 200."* | author | |
| S03-033 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> gives has_word a blank word; has_word finds one word or phrase."* | author | |
| S03-034 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> uses if without an else; write the else."* | author | |
| S03-035 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> cats \<path\> (\<format\>); convert Markdown with plain first."* | author | |
| S03-036 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"Node '\<id\>' declares \<modelDerived \| runDependent \| profileCarrying\> \<value\>, but its reads make it \<derived\>: \<the read that does, or none\>."* | author | |
| S03-037 | [11.2 Warnings at publish and in the Editor](03-expressions.md#112-warnings-at-publish-and-in-the-editor) | *"\<site\> \<member\> reads run.\<fact\>; its decision can differ between runs with the same inputs."* | author | |
| S03-038 | [11.2 Warnings at publish and in the Editor](03-expressions.md#112-warnings-at-publish-and-in-the-editor) | *"Computed node '\<id\>' reads \<run.fact or path\>, which is run-dependent; its value can differ between runs with the same inputs."* | author | |
| S03-039 | [11.2 Warnings at publish and in the Editor](03-expressions.md#112-warnings-at-publish-and-in-the-editor) | *"computed from model text: node.\<id\>"* | author | |
| S03-040 | [11.3 Failures at run time](03-expressions.md#113-failures-at-run-time) | *"\<site\> \<member\> could not be decided: matches was given \<n\> MiB of text; the most is 1 MiB."* | author | |
| S03-041 | [11.3 Failures at run time](03-expressions.md#113-failures-at-run-time) | *"\<site\> \<member\> yields \<a value kind\>; a condition yields a boolean."* | author | |
| S03-042 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> pattern "\<pattern\>" repeats \<n\> times; a counted repetition is at most 100."* | author | |
| S03-043 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> pattern "\<pattern\>" doesn't compile with the engine's options: \<reason\>."* | author | |
| S03-044 | [11.1 Refusals at publish](03-expressions.md#111-refusals-at-publish) | *"\<site\> reads \<path\>, which carries profile text; prompts, parts and conditions don't read profile text."* | author | |
## 04 Loops and functions

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S04-001 | [2.4 `until`, `max` and `.stopped`](04-loops-and-functions.md#24-until-max-and-stopped) | *"stopped early at \<position name\>"* | clinician | |
| S04-002 | [2.4 `until`, `max` and `.stopped`](04-loops-and-functions.md#24-until-max-and-stopped) | *"not run: beyond max \<n\>"* | clinician | |
| S04-003 | [2.6 `item` and the `.items` element](04-loops-and-functions.md#26-item-and-the-items-element) | *"skipped: item.value.kind = "narrative" is false"* | clinician | |
| S04-004 | [2.7 `itemLabel`: naming items](04-loops-and-functions.md#27-itemlabel-naming-items) | *"Sections 2"* | author | |
| S04-005 | [3.2 How it runs](04-loops-and-functions.md#32-how-it-runs) | *"stopped at its bound of 3; the check never stopped holding"* | clinician | |
| S04-006 | [5.2 `case`](04-loops-and-functions.md#52-case) | *"case '\<id\>': arm for \<value\> does nothing"* | author | |
| S04-007 | [5.4 Examples (migrated)](04-loops-and-functions.md#54-examples-migrated) | *"case 'route': arm for omit does nothing"* | author | |
| S04-008 | [5.4 Examples (migrated)](04-loops-and-functions.md#54-examples-migrated) | *"skipped: item.id ≠ "billing" or input.include_billing.value = true is false"* | clinician | |
| S04-009 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares label null; a loop's or branch's label is what the rail, History and the diagram show: write one."* | author | |
| S04-010 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<Kind\> '\<id\>' declares \<member\>, which belongs to \<a for \| a while \| an if \| a case\>."* | author | |
| S04-011 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<subject\> returns nothing; name the body node the outside reads with ret."* | author | |
| S04-012 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<subject\> returns \<ret\>, which is not one of its own children (\<full names\>): write the child's full name, for example node.sections.paragraphs."* | author | |
| S04-013 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<subject\> lists node '\<id\>' twice; a body is a set."* | author | |
| S04-014 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<Kind\> '\<id\>' declares format \<format\>; its ret makes it \<format\>."* | author | |
| S04-015 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<Kind\> '\<id\>' declares reproducible \<true \| false\>; its body makes it \<false \| true\>."* | author | |
| S04-016 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<Kind\> '\<id\>' declares \<modelDerived \| runDependent \| profileCarrying\> \<true \| false\>; its body makes it \<false \| true\>."* | author | |
| S04-017 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<Kind\> '\<id\>' \<in \| case \| with \<param\>\> is an expression; it names one readable thing: compute the value in a computed node and name that."* | author | |
| S04-018 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' has no body; name the nodes that run per item in nodes."* | author | |
| S04-019 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares parallel true with maxParallel 1, which runs one item at a time; declare "parallel": false and "maxParallel": null instead."* | author | |
| S04-020 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares maxParallel \<n\>; the most is FormatBounds.parallel (15)."* | author | |
| S04-021 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares maxParallel \<n\>; the least is 2, or null for no cap of its own."* | author | |
| S04-022 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares maxParallel \<n\> with parallel false; a sequential loop runs one item at a time: write "maxParallel": null."* | author | |
| S04-023 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares until with parallel true; until stops after the item where it holds, which needs items in order: declare "parallel": false and "maxParallel": null."* | author | |
| S04-024 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' declares max 0; the least is 1."* | author | |
| S04-025 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' iterates \<reference\>, which is \<format\>; only a list can be iterated."* | author | |
| S04-026 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' iterates item.value.\<field\>, but it runs inside no loop; item.value names the enclosing loop's item."* | author | |
| S04-027 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' iterates node.\<y\>.value.\<z\>; \<y\> runs inside loop '\<l\>' — a loop iterates a node outside it, or in an enclosing loop's body."* | author | |
| S04-028 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' itemLabel '\<field\>' is not a field of the elements of \<reference\> (fields: \<fields\>)."* | author | |
| S04-029 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' itemLabel '\<field\>' may be missing or null; itemLabel names a field every element has as a string."* | author | |
| S04-030 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Loop '\<id\>' itemLabel '\<field\>' is \<format\>; itemLabel names a string field."* | author | |
| S04-031 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"While '\<id\>' declares max null; a while must declare its bound (1 to 15)."* | author | |
| S04-032 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"While '\<id\>' declares max \<n\>; a while runs at most FormatBounds.passes (15) passes."* | author | |
| S04-033 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"While '\<id\>' declares max \<n\>; the least is 1."* | author | |
| S04-034 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"While '\<id\>' has no body; name the nodes that run per pass in nodes."* | author | |
| S04-035 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Node '\<id\>' in while '\<w\>' is neither returned nor read; name it with ret, read it from a body node or from the check."* | author | |
| S04-036 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<site\> reads pass.number, but it runs inside no while; pass is a while's pass."* | author | |
| S04-037 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<site\> reads pass.parent, but its while runs inside no while; pass.parent is the enclosing while's pass."* | author | |
| S04-038 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<site\> reads pass.previous.node.\<id\>, which is not in while '\<w\>'; a pass reads the while's own nodes."* | author | |
| S04-039 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<site\> reads node '\<id\>', which runs inside loop '\<l\>'; read node.\<l\>."* | author | |
| S04-040 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"\<site\> reads item.parent, but its loop runs inside no loop; item.parent is the enclosing loop's item."* | author | |
| S04-041 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"If '\<id\>' declares then null; an if runs a then body."* | author | |
| S04-042 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' has no branches; a case runs one branch per set of values."* | author | |
| S04-043 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' branch \<n\> lists no value; a branch's when names one or more values."* | author | |
| S04-044 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' switches on \<reference\>, which is \<format\>; a case switches on an enumerated string."* | author | |
| S04-045 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' branch \<n\> lists "\<value\>", which is not one of \<reference\>'s values (\<values\>)."* | author | |
| S04-046 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' lists "\<value\>" in branches \<n\> and \<m\>; a value belongs to one branch."* | author | |
| S04-047 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' doesn't list \<values\> and has no else; a case has a branch for every value: add a branch, an empty branch, or an else."* | author | |
| S04-048 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' branch \<n\> writes nodes \<[] \| ids\> with ret \<ret\>; an empty branch writes nodes [] and ret null together."* | author | |
| S04-049 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"case '\<id\>': arm for \<value\> does nothing"* | author | |
| S04-050 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Node '\<id\>' in function '\<f\>' reads input.\<name\>; a function reads only its parameters: add a parameter and pass it in with."* | author | |
| S04-051 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Node '\<id\>' in function '\<f\>' reads \<reference\>; a function reads only its parameters: add a parameter and pass it in with."* | author | |
| S04-052 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' uses \<snippet \| macro \| format \| renderer\> '\<id\>' but doesn't list it in uses."* | author | |
| S04-053 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' has its own \<snippet \| macro \| format \| renderer\> '\<id\>' and also uses the package's: rename one."* | author | |
| S04-054 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<a\>' calls '\<b\>', which calls '\<a\>': functions may not call themselves."* | author | |
| S04-055 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<a\>' calls '\<a\>': functions may not call themselves."* | author | |
| S04-056 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' reaches depth \<n\>; calls nest at most FormatBounds.depth (15) deep."* | author | |
| S04-057 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' returns node.\<id\>, an if with no else: add an else, or return a node that always runs."* | author | |
| S04-058 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' returns node.\<id\>, which runs inside if '\<b\>' and may be skipped: return a node that always runs."* | author | |
| S04-059 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' returns node.\<id\>, a case with an empty branch for \<values\>: return a node that always runs."* | author | |
| S04-060 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' returns node.\<id\>, whose branches return \<format\> and \<format\>: a function returns one format. Wrap them in an object format."* | author | |
| S04-061 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' declares format \<format\>; its ret '\<id\>' returns \<format\>."* | author | |
| S04-062 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' holds a \<checks \| deliverables\> folder; checks and deliverables live only at the package's top level: return a check_result or a value instead."* | author | |
| S04-063 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Macro '\<m\>' is placed in function '\<f\>', where its if reads node.\<id\>, which doesn't exist there: add a classifier with that id (pattern A), or pass the decision in and hold it in a computed node (pattern B)."* | author | |
| S04-064 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' names function '\<f\>', which the package doesn't define."* | author | |
| S04-065 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' passes no \<param\>; function '\<f\>' takes \<params\>."* | author | |
| S04-066 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' passes \<param\>, which function '\<f\>' doesn't take (\<params\>)."* | author | |
| S04-067 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<f\>' parameter '\<param\>' declares \<required \| channel\>; a parameter has label, description and format, and every one is passed."* | author | |
| S04-068 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' passes \<reference\> (\<format\>) as \<param\>; function '\<f\>' takes \<format\>."* | author | |
| S04-069 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Function '\<id\>' reads param.\<name\>, which carries profile.\<field\>, in \<a prompt \| a part \| a condition\>; the profile is read only in deliverables and macro bodies."* | author | |
| S04-070 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' declares format \<format\>; function '\<f\>' returns \<format\>."* | author | |
| S04-071 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Call '\<id\>' has a body; a call is a header only."* | author | |
| S04-072 | [10.2 Notes and warnings at publish](04-loops-and-functions.md#102-notes-and-warnings-at-publish) | *"Up to \<n\> agent calls could run at once; this engine runs at most \<N\> per job, so the rest wait their turn."* | author | |
| S04-073 | [10.2 Notes and warnings at publish](04-loops-and-functions.md#102-notes-and-warnings-at-publish) | *"An unbounded number of agent calls could run at once (loop '\<id\>' has no cap over a list known only at run time); this engine runs at most \<N\> per job, so the rest wait their turn."* | author | |
| S04-074 | [10.2 Notes and warnings at publish](04-loops-and-functions.md#102-notes-and-warnings-at-publish) | *"Function '\<f\>' lists \<snippet \| macro \| format \| renderer\> '\<id\>' in uses but never uses it."* | author | |
| S04-075 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' switches on \<reference\>, which can be null, and has no else; a null subject goes to the else: add one."* | author | |
| S04-076 | [10.1 Refusals at publish](04-loops-and-functions.md#101-refusals-at-publish) | *"Case '\<id\>' branch \<n\> lists null; a null subject goes to the else."* | author | |
| S04-077 | [2.4 `until`, `max` and `.stopped`](04-loops-and-functions.md#24-until-max-and-stopped) | *"0 items: \<reference\> is null"* | clinician | |
| S04-078 | [2.4 `until`, `max` and `.stopped`](04-loops-and-functions.md#24-until-max-and-stopped) | *"Loop '\<id\>' was given \<n\> items; this engine runs at most \<limit\> per loop."* | clinician | |
| S04-079 | [10.2 Notes and warnings at publish](04-loops-and-functions.md#102-notes-and-warnings-at-publish) | *"Loop '\<id\>' iterates \<reference\>, which could hold more than \<limit\> items; this engine runs at most \<limit\> per loop, and fails the loop beyond that."* | author | |
| S04-080 | [10.2 Notes and warnings at publish](04-loops-and-functions.md#102-notes-and-warnings-at-publish) | *"Up to \<n\> agent calls could run in one job; this engine runs at most \<limit\> per job, and fails the job beyond that."* | author | |
## 05 Templates and macros

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S05-001 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> uses \<construct\> at line \<n\>; templates present text and may not use assignment, capture, func, include, import, with, wrap, while, break, continue, case, tablerow or arithmetic."* | author | |
| S05-002 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> calls \<built-in\> at line \<n\>; the allowed built-ins are string.upcase, string.downcase, string.capitalize, string.strip, array.join, array.size and date.to_string."* | author | |
| S05-003 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> calls truncate at line \<n\>; truncate cuts clinical text."* | author | |
| S05-004 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> calls object.default at line \<n\>; choosing text is a decision: put it in a header condition."* | author | |
| S05-005 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> calls date.to_string at line \<n\> without a pattern; write the pattern, for example "%d %B %Y"."* | author | |
| S05-006 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> runs a for over \<expr\> at line \<n\>; a for runs over a list."* | author | |
| S05-007 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> tests \<expr\> in a body if at line \<n\>; a body if tests only loop position, state and whether a check passed. Decide it in a header: a part, a snippet's if or a macro's if."* | author | |
| S05-008 | [Refusals](05-templates-and-macros.md#refusals) | *"Deliverable 'consult' branches on part.state and handles completed and skipped, but not not_run: add it or an else."* | author | |
| S05-009 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> branches on \<expr\> and handles \<states\>, but not \<missing\>: add it or an else."* | author | |
| S05-010 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> compares \<expr\> with "\<value\>" at line \<n\>; a state is completed, skipped or not_run."* | author | |
| S05-011 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads \<path\> at line \<n\>, which \<format or root\> doesn't have."* | author | |
| S05-012 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> stopped rendering: the for at line \<n\> reached 1,000 iterations."* | author | |
| S05-013 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> nests if and for \<n\> deep at line \<m\>; the limit is 15."* | author | |
| S05-014 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> stopped rendering: its output passed 1 MiB."* | author | |
| S05-015 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> places \<unit\> with ~}} at line \<n\>; it removes the line's newline, joining two lines. Use {{~ \<unit\> }} alone on a line."* | author | |
| S05-016 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> places \<unit\> with - at line \<n\>; it removes blank lines between paragraphs."* | author | |
| S05-017 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> places {{~ \<unit\> }} inside a line at line \<n\>; it removes the space before it. Use {{ \<unit\> }}."* | author | |
| S05-018 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> has text after {{~ \<unit\> }} at line \<n\>; a block placement stands alone on its line."* | author | |
| S05-019 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> uses - at line \<n\>; it removes blank lines between paragraphs. Use ~."* | author | |
| S05-020 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads \<root\> at line \<n\>; \<site\> reads \<allowed roots\>."* | author | |
| S05-021 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' reads profile.\<field\>; the clinician's profile is never sent to a model. Read it in a deliverable or a macro body."* | author | |
| S05-022 | [Refusals](05-templates-and-macros.md#refusals) | *"Snippet '\<id\>' reads \<path\>, which isn't available where \<subject\> places it at line \<n\>."* | author | |
| S05-023 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Kind\> '\<id\>' reaches itself: \<chain\>."* | author | |
| S05-024 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads \<path\> and run.\<fact\> in one output at line \<n\>; an output that reads a run fact reads nothing else."* | author | |
| S05-025 | [Refusals](05-templates-and-macros.md#refusals) | *"… reads \<path\> and the run-dependent \<value\> in one output …"* | author | |
| S05-026 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> pipes \<source\> (\<format\>) into renderer.\<id\>, which renders \<input format\>."* | author | |
| S05-027 | [Refusals](05-templates-and-macros.md#refusals) | *"Node 'identify_problem' pipes node.plan.value (section_plan) into renderer.concept_bullets, which renders concept_list."* | author | |
| S05-028 | [Refusals](05-templates-and-macros.md#refusals) | *"Renderer '\<id\>' declares format \<format\>; a renderer returns plaintext, markdown_block or markdown_inline."* | author | |
| S05-029 | [Refusals](05-templates-and-macros.md#refusals) | *"Renderer '\<id\>' reads \<root\>; a renderer reads only the value piped into it."* | author | |
| S05-030 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> calls renderer.\<id\> at line \<n\>; pipe into it: \<value\> \| renderer.\<id\>."* | author | |
| S05-031 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> pipes into renderer.\<id\>, which this package doesn't define."* | author | |
| S05-032 | [Refusals](05-templates-and-macros.md#refusals) | *"Renderer '\<id\>' takes the id of a built-in renderer."* | author | |
| S05-033 | [Refusals](05-templates-and-macros.md#refusals) | *"Renderer '\<id\>' pipes into renderer.\<other\> at line \<n\>; chain renderers where they're used, not inside a renderer."* | author | |
| S05-034 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> pipes renderer.\<id\> (\<format\>) into renderer.\<next\>, which renders \<input format\>."* | author | |
| S05-035 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> places snippet.\<id\>, which this package doesn't define."* | author | |
| S05-036 | [Refusals](05-templates-and-macros.md#refusals) | *"Snippet '\<folder\>/\<id\>' has a header that doesn't match format \<format\>: \<schema error\>."* | author | |
| S05-037 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads snippet.\<id\> at line \<n\>; read its text as snippet.\<id\>.content."* | author | |
| S05-038 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' places part.\<name\>, which its header doesn't declare."* | author | |
| S05-039 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' declares part '\<name\>' but never places it."* | author | |
| S05-040 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' places part.\<name\> \<n\> times; place a part once."* | author | |
| S05-041 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' declares parts, and only agent nodes have parts."* | author | |
| S05-042 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' places macro.\<other\> in its body; a macro body is output text, written after the model replies. Put shared text in a snippet and place it in both."* | author | |
| S05-043 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' places part.\<name\> in its body; …"* | author | |
| S05-044 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' has \<n\> markers in its instruction; it must hold {{~ macro.\<id\>.content }} or {{ macro.\<id\>.content }} exactly once."* | author | |
| S05-045 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' has the marker of macro '\<other\>' in its instruction."* | author | |
| S05-046 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' places macro.\<macro\>, whose format is \<format\>; an \<node format\> node takes \<allowed\>."* | author | |
| S05-047 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> places macro.\<id\>; only agent nodes place macros, in their prompt or a part's text."* | author | |
| S05-048 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> places macro.\<id\> twice, counting its parts' text; place a macro once in a template."* | author | |
| S05-049 | [Refusals](05-templates-and-macros.md#refusals) | *"Node '\<id\>' places macro.\<macro\>, and returns \<format\>; macros are placed only in agent nodes returning a text format."* | author | |
| S05-050 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' has format \<format\>, and its body \<reason, e.g. writes Markdown at line 2\>."* | author | |
| S05-051 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' has a marker in its body; a body is the text that replaces the marker."* | author | |
| S05-052 | [Refusals](05-templates-and-macros.md#refusals) | *"Macro '\<id\>' reads param.\<name\> in its if; decide outside the function and pass the answer in, or classify inside it."* | author | |
| S05-053 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply has no marker for macro '\<id\>'."* | author | |
| S05-054 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply has the marker for macro '\<id\>' \<n\> times."* | author | |
| S05-055 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply has a marker for macro '\<id\>', which wasn't offered."* | author | |
| S05-056 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply has a marker for '\<id\>', which isn't a macro."* | author | |
| S05-057 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply writes the marker for macro '\<id\>' \<inside a sentence \| alone on a line\>, but it was asked for \<alone on a line \| inside a sentence\>."* | author | |
| S05-058 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply has text after the marker for macro '\<id\>' on its line."* | author | |
| S05-059 | [Refusals](05-templates-and-macros.md#refusals) | *"The reply has the markers out of order: '\<id\>' before '\<other\>'."* | author | |
| S05-060 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads \<run.fact or run-dependent value\> at line \<n\>. Its main hash differs between runs; runs are compared on the run-neutral hash."* | author | |
| S05-061 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> pipes renderer.\<id\>, a markdown_block, inside a line at line \<n\>."* | author | |
| S05-062 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> stopped rendering: it took more than \<limit\> steps."* | author | |
| S05-063 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads \<path\>[\<n\>] at line \<n\>, a position in a list; lists are read whole, with a for, or by identity through .items."* | author | |
| S05-064 | [Refusals](05-templates-and-macros.md#refusals) | *"\<Subject\> reads \<path\> at line \<n\>, which carries profile text through macro '\<id\>'; a prompt or a part doesn't read profile text. Read it in a deliverable."* | author | |
## 06 Checks, deliverables and the profile

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S06-001 | [5. Checks in the record and History](06-checks-deliverables-profile.md#5-checks-in-the-record-and-history) | *"12 of 12 terms present"* | author | |
| S06-002 | [5. Checks in the record and History](06-checks-deliverables-profile.md#5-checks-in-the-record-and-history) | *"7 of 9 sections passed"* | author | |
| S06-003 | [5. Checks in the record and History](06-checks-deliverables-profile.md#5-checks-in-the-record-and-history) | *"Consultation note was blocked: check 'coverage' failed (10 of 12 terms present)."* | clinician | |
| S06-004 | [6.2 Item states](06-checks-deliverables-profile.md#62-item-states) | *"stopped early at \<position name\>"* | clinician | |
| S06-005 | [6.5 Headings are the author's](06-checks-deliverables-profile.md#65-headings-are-the-authors) | *"computed from model text: node.standards.section_instructions"* | author | |
| S06-006 | [7. Gating and outcomes](06-checks-deliverables-profile.md#7-gating-and-outcomes) | *"released: check '\<id\>' didn't apply (its if was false)"* | clinician | |
| S06-007 | [8. Delivery](06-checks-deliverables-profile.md#8-delivery) | *"not sent: EMR isn't set up for this account"* | clinician | |
| S06-008 | [10.7 The Profile page and the time zone](06-checks-deliverables-profile.md#107-the-profile-page-and-the-time-zone) | *"Set your time zone in your account settings before running a workflow."* | clinician | |
| S06-009 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Check '\<id\>' has format \<format\>; a check returns check_result."* | author | |
| S06-010 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Check '\<id\>' has kind \<kind\>; a check is computed."* | author | |
| S06-011 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"\<Path\> in checks/ is \<kind\>; checks/ holds checks and the loops over them."* | author | |
| S06-012 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Check '\<id\>' passes \<expr\> as failWith; failWith is a literal sentence."* | author | |
| S06-013 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Check '\<id\>' passes \<expr\> as min; min is a whole number of at least 0."* | author | |
| S06-014 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Check loop '\<id\>' runs over \<source\>; a check loop runs over a production loop's .items."* | author | |
| S06-015 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Function '\<id\>' holds check '\<check\>'; functions hold no checks. Return a check_result as data and check the calls' .items."* | author | |
| S06-016 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Node '\<id\>' reads deliverable.\<deliverable\>; only checks read deliverables."* | author | |
| S06-017 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Node '\<id\>' reads deliverable.\<deliverable\>; deliverables are the job's outputs: read the nodes it is composed from."* | author | |
| S06-018 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' reads check.\<check\>, which checks \<id\>: a deliverable can't read its own check."* | author | |
| S06-019 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' reads deliverable.\<other\>; a deliverable reads nodes, inputs, snippets, checks, the profile and run facts."* | author | |
| S06-020 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' has format \<format\>; a deliverable is markdown_block, plaintext or an object format."* | author | |
| S06-021 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' is \<format\>, and reads \<path\> (\<read format\>) at line \<n\>. \<Convert it in a node \| Use a renderer or a node\>."* | author | |
| S06-022 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' is plaintext, and line \<n\> writes Markdown ('\<text\>')."* | author | |
| S06-023 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' is \<object format\>; its template is {{ node.\<x\>.value }} and nothing else."* | author | |
| S06-024 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' reads \<path\> in its if; a deliverable's if reads inputs, node values and run facts."* | author | |
| S06-025 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverable '\<id\>' has whenEmpty \<value\>; write release, hold or fail."* | author | |
| S06-026 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Deliverables folder '\<path\>' has delivery kind \<kind\>; the kinds are email, pdf, emr and zip."* | author | |
| S06-027 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"deliverables/index.json is missing; the deliverables folder states its delivery."* | author | |
| S06-028 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"\<Subject\> reads profile.\<field\>, which isn't a profile field; the fields are name, credentials, organisation, signature and closing."* | author | |
| S06-029 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"\<Condition owner\> reads profile.\<field\> in a condition; the profile is read only in deliverables and macro bodies."* | author | |
| S06-030 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Function '\<id\>' reads param.\<name\>, which carries profile.\<field\>, in \<a prompt \| a part \| a condition\>; the profile is read only in deliverables and macro bodies."* | author | |
| S06-031 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"This workflow needs your signature. Choose one in Profile."* | clinician | |
| S06-032 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"This workflow needs your \<field label\>. Set it in Profile, under Letter details."* | clinician | |
| S06-033 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"\<Deliverable label\> was blocked: check '\<id\>' failed (\<detail\>)."* | author | |
| S06-034 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"not sent: \<Kind\> isn't set up for this account"* | clinician | |
| S06-035 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"Check '\<id\>' gates deliverable '\<deliverable\>', and its if reads \<path\>, which can be null; a gate check's if reads values that are always set."* | author | |
| S06-036 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"\<Subject\> reads \<path\>, which carries profile text; prompts, parts and conditions don't read profile text. Read it in a deliverable."* | author | |
| S06-037 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"\<Deliverable label\> was blocked: check '\<id\>' could not run (\<error\>)."* | clinician | |
| S06-038 | [Refusals](06-checks-deliverables-profile.md#refusals) | *"not sent: the job failed"* | clinician | |
## 07 The compiled documents

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S07-001 | [4.4 Upgrading a locked library: the corpus re-render](07-compiled-documents.md#44-upgrading-a-locked-library-the-corpus-re-render) | *"Scriban output changed at 7.3.0"* | author | |
| S07-002 | [6. Where the files live](07-compiled-documents.md#6-where-the-files-live) | *"Download package"* | author | |
| S07-003 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"This workflow (\<package\>@\<version\>) needs \<component\> \<range\>; this engine runs \<component\> \<engine version\>. Publish a new version of the workflow to run it on this engine."* | clinician | |
| S07-004 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"This workflow (\<package\>@\<version\>) needs \<kind or feature\> '\<name\>', which this engine doesn't support. Publish a new version of the workflow to run it on this engine."* | clinician | |
| S07-005 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"This workflow (\<package\>@\<version\>) needs agent \<agent name\> version \<version\>, which this engine doesn't have. Publish a new version of the workflow to run it on this engine."* | clinician | |
| S07-006 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Workflow \<package\>@\<version\> can't run: its compiled document doesn't match the hash published for it."* | author | |
| S07-007 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' failed: \<expectation\> for \<key\> expected \<expected\>, got \<actual\>. Publish stopped."* | author | |
| S07-008 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' has no stub for \<row key\>, which runs. Add it to outputs."* | author | |
| S07-009 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' stubs \<row key\> with a value that isn't \<format\>: \<first validation error\>."* | author | |
| S07-010 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' gives no value for input '\<input id\>'. List every input; use null for an optional input left unset."* | author | |
| S07-011 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' must give exactly the profile fields the workflow reads: \<fields\>."* | author | |
| S07-012 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' must give exactly the run facts the workflow reads: \<facts\>."* | author | |
| S07-013 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Tests took longer than 60 seconds in all. Publish stopped."* | author | |
| S07-014 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Test '\<test id\>' stubs \<row key\>, which doesn't run."* | author | |
| S07-015 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"\<path\> is evidence written only by publish; it is ignored and shouldn't be committed."* | author | |
| S07-016 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"This package's resolve.json and lock.json were ignored; publishing writes new ones."* | author | |
| S07-017 | [10. Refusals](07-compiled-documents.md#10-refusals) | *"Workflow \<package\>@\<version\> can't run: its lock doesn't match the hash published for it, or doesn't belong to its compiled document."* | clinician | |
## 08 The record

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S08-001 | [4. Node rows](08-record.md#4-node-rows) | *"skipped: …"* | clinician | |
| S08-002 | [4. Node rows](08-record.md#4-node-rows) | *"not run: beyond max \<n\>"* | clinician | |
| S08-003 | [10. Deliverable rows](08-record.md#10-deliverable-rows) | *"held: empty"* | clinician | |
| S08-004 | [10. Deliverable rows](08-record.md#10-deliverable-rows) | *"skipped: \<condition\> is false"* | clinician | |
| S08-005 | [Refusals and recorded sentences](08-record.md#refusals-and-recorded-sentences) | *"skipped: \<printed condition\> is false"* | clinician | |
| S08-006 | [Refusals and recorded sentences](08-record.md#refusals-and-recorded-sentences) | *"not sent: \<kind\> isn't set up for this account"* | clinician | |
| S08-007 | [Refusals and recorded sentences](08-record.md#refusals-and-recorded-sentences) | *"blocked: check '\<id\>' failed (\<detail\>)"* | clinician | |
| S08-008 | [Refusals and recorded sentences](08-record.md#refusals-and-recorded-sentences) | *"blocked: check '\<id\>' could not run (\<error\>)"* | clinician | |
| S08-009 | [Refusals and recorded sentences](08-record.md#refusals-and-recorded-sentences) | *"cancelled: the job didn't finish within \<n\> minutes"* | clinician | |
## 09 The engine and the pages

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S09-001 | [1.3 Which spec versions an engine admits](09-engine-and-pages.md#13-which-spec-versions-an-engine-admits) | *"specVersion 31 is not supported: this engine accepts specVersion \<list\> (pre-v5 packages are archived; see registry-operations.md)."* | author | |
| S09-002 | [3.5 The publish note](09-engine-and-pages.md#35-the-publish-note) | *"Up to \<n\> agent calls could run at once; this engine runs at most \<ceiling\> per job, so the \> rest wait their turn."* | author | |
| S09-003 | [3.5 The publish note](09-engine-and-pages.md#35-the-publish-note) | *"An unbounded number of agent calls could run at once, since \<loop key\> has no maxParallel and \> reads a list of unknown length; this engine runs at most \<ceiling\> per job, so the rest wait \> their turn."* | author | |
| S09-004 | [6.2 The job header](09-engine-and-pages.md#62-the-job-header) | *"migrated by the operator from \<old version\>"* | author | |
| S09-005 | [6.3 Banners](09-engine-and-pages.md#63-banners) | *"\<deliverable label\> was blocked: check '\<check id\>' failed (\<check detail\>)."* | author | |
| S09-006 | [6.4 The sections, in the order the job runs](09-engine-and-pages.md#64-the-sections-in-the-order-the-job-runs) | *"16 November 2026"* | author | |
| S09-007 | [8.1 31 packages are view-only until Milestone 41](09-engine-and-pages.md#81-31-packages-are-view-only-until-milestone-41) | *"This package uses the new format. You can view and download it; editing returns in a later release."* | author | |
| S09-008 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"This workflow (example-nested-note@v2026.11.2) needs Scriban ^7.2; this engine runs Scriban 8.0.1. Publish a new version of the workflow to run it on this engine."* | clinician | |
| S09-009 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"The compiled workflow \<package\>@\<version\> doesn't match its published hash, so it can't be run. Tell the operator."* | clinician | |
| S09-010 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"The model service stayed busy for \<n\> minutes. Run the workflow again later."* | clinician | |
| S09-011 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"The model service \<timed out \| returned an error \| couldn't be reached\> \<n\> times in a row. Run the workflow again later."* | clinician | |
| S09-012 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"The job didn't finish within \<n\> minutes. Run the workflow again later."* | clinician | |
| S09-013 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"Up to \<n\> agent calls could run at once; this engine runs at most \<ceiling\> per job, so the rest wait their turn."* | author | |
| S09-014 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"The compiled workflow \<package\>@\<version\> doesn't match its lock, so it can't be run. Tell the operator."* | clinician | |
| S09-015 | [9. Refusals and notes](09-engine-and-pages.md#9-refusals-and-notes) | *"The job reached \<limit\> agent calls, the most this engine runs for one job."* | clinician | |
| S09-016 | [3.5 The publish note](09-engine-and-pages.md#35-the-publish-note) | *"\<package\> could run for about \<n\> minutes in the worst case; this engine stops a job after \<limit\> minutes."* | author | |
| S09-017 | [3.8 Delivery at the end](09-engine-and-pages.md#38-delivery-at-the-end) | *"Couldn't send to \<destination\>. Copy the note instead."* | clinician | |
## 10 Migration and tools

| Id | Section | Sentence | Audience | Review |
|---|---|---|---|---|
| S10-001 | [1.4 Hand edits, with notices](10-migration-and-tools.md#14-hand-edits-with-notices) | *"written; hand edits remain"* | author | |
| S10-002 | [1.4 Hand edits, with notices](10-migration-and-tools.md#14-hand-edits-with-notices) | *"Hand edit needed in \<file\>, \<member\>: \<reason\>."* | author | |
| S10-003 | [Refusals](10-migration-and-tools.md#refusals) | *"The condition '\<expression\>' uses \<construct\>, which has no JSON Logic form at 31: rewrite it by hand."* | author | |
| S10-004 | [Refusals](10-migration-and-tools.md#refusals) | *"the prompt reads profile.\<field\>, which a 31 prompt doesn't read; move it to a deliverable or a macro body."* | author | |
| S10-005 | [Refusals](10-migration-and-tools.md#refusals) | *"\<package\> stays at version 30 for now: \<reason\>. Its pin is unchanged; you can migrate it in the Editor when Milestone 41 arrives."* | clinician | |
| S10-006 | [Refusals](10-migration-and-tools.md#refusals) | *"Renamed '\<id\>' to '\<new id\>'; '\<id\>_\<kind\>' was already taken."* | author | |
