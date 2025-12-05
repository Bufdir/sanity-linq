# Authoring Best Practices

This guide serves two audiences:

- Consumers: developers using `Sanity.Linq` to query and mutate Sanity CMS.
- Contributors: developers working on the `Sanity.Linq` library itself.

Use the table of contents to jump to what you need.

## Table of Contents
- Consumers (Using Sanity.Linq)
  - Getting the most out of LINQ queries
  - Projections and shaping data
  - Joins and references
  - Pagination and performance
  - Mutations, transactions, and idempotency
  - Error handling and resilience
  - Configuration tips
- Contributors (Working on the library)
  - Coding conventions and public API guidelines
  - Exceptions and error design
  - Testing strategy
  - Versioning and breaking-change policy
  - Contribution workflow and CI

---

## Consumers (Using Sanity.Linq)

### Getting the most out of LINQ queries

- Prefer server-side filtering. Always place filters inside the query before materialization methods like `ToListAsync()`, `FirstOrDefaultAsync()`, or `CountAsync()`.
  ```csharp
  var posts = sanity.DocumentSet<Post>();
  var publishedToday = await posts
      .Where(p => p.PublishedAt > DateTime.Today)
      .ToListAsync();
  ```
- Avoid client-side evaluation. Make sure predicates are translatable to GROQ. If you see unexpected results, check the generated query using tests in `SanityQueryBuilderTests` as a reference for patterns the provider supports.

### Projections and shaping data

- Fetch only what you need using `Select` to project to a smaller shape. This reduces payload size and speeds up queries.
  ```csharp
  var summaries = await sanity.DocumentSet<Post>()
      .Where(p => p.PublishedAt != null)
      .OrderByDescending(p => p.PublishedAt)
      .Select(p => new { p.Id, p.Title, p.PublishedAt })
      .ToListAsync();
  ```
- When mapping to your own DTOs, keep property names aligned with your target model. For POCOs not inheriting `SanityDocument`, use `JsonProperty` where necessary to map `_id` and `_type`.

### Joins and references

- Use navigation via references supported by the provider to avoid n+1 fetches. When you need denormalized views, project the referenced fields you actually need.
- If you find yourself repeatedly joining large documents, consider storing a summary field in the referencing document to reduce query cost.

### Pagination and performance

- Use `OrderBy` with `Skip`/`Take` for stable pagination.
  ```csharp
  var page = await sanity.DocumentSet<Post>()
      .OrderByDescending(p => p.PublishedAt)
      .Skip(pageIndex * pageSize)
      .Take(pageSize)
      .Select(p => new { p.Id, p.Title })
      .ToListAsync();
  ```
- Prefer projections when paginating to avoid transferring large document bodies.
- For read-heavy, cache-friendly scenarios, set `UseCdn = true`. For strongly consistent reads (immediately after writes), use `UseCdn = false`.

### Mutations, transactions, and idempotency

- Group related changes in a transaction when consistency matters.
  ```csharp
  await sanity.Mutations()
      .BeginTransaction()
      .Create(new Post { Title = "Hello" })
      .Patch<Post>(id: someId, patch => patch.Set(p => p.Title, "Updated"))
      .CommitAsync();
  ```
- Use stable client-generated IDs for upserts when appropriate to achieve idempotency.
- Prefer `PatchByQuery` for bulk updates with a precise filter. Test on a small dataset first.

### Error handling and resilience

- Catch `SanityHttpException` for HTTP/API errors and inspect `StatusCode` and response content to decide on retries or user messaging.
- Catch `SanityDeserializationException` when the response shape does not match your model. Review your projection or model attributes (`JsonProperty` for `_id` and `_type`) to resolve.
- Implement conservative retries for transient failures (e.g., 429/5xx) with exponential backoff. Keep mutation retries idempotent to avoid duplicate writes.

### Configuration tips

- Always set `ApiVersion` explicitly in `SanityOptions` to ensure consistent behavior across time.
- For production, prefer `HttpClient` reuse via dependency injection to avoid socket exhaustion.
- Keep tokens with the minimal required scope; avoid using write tokens in read-only services.

---

## Contributors (Working on the library)

### Coding conventions and public API guidelines

- Target frameworks: `netstandard2.1` and `net10.0` are currently used. Verify compatibility for changes to shared code.
- Nullable: Prefer enabling nullable reference types in new/edited files. Where legacy code uses `#nullable disable`, do not mix models mid-file; migrate opportunistically with dedicated changes.
- Style: Follow existing code layout and naming. Keep using expression-bodied members and pattern matching where already present.
- Public API:
  - Keep APIs predictable and LINQ-centric. Avoid surprising side effects during enumeration.
  - Provide async variants (`...Async`) for operations that perform I/O; prefer async-first implementation.
  - Document public members with XML docs sufficient for IntelliSense.

### Exceptions and error design

- Throw `SanityHttpException` for HTTP-level failures; include relevant context (status, request URI, response snippet) without leaking secrets.
- Throw `SanityDeserializationException` for payload-to-model issues; include the failing type and a short excerpt of the payload when safe.
- Do not swallow exceptions. Convert to domain-specific exceptions only when it adds value.

### Testing strategy

- Place tests in `tests/Sanity.Linq.Tests` and keep them deterministic.
- Add unit tests for query translation in `SanityQueryBuilderTests` when modifying the expression parser or builder.
- Add tests for HTTP client behavior when touching `SanityClient`/`SanityDataContext` and exception flows.
- Provide minimal demo updates in `demo/Sanity.Linq.Demo` only when examples benefit users; avoid tight coupling of tests to the demo.

### Versioning and breaking-change policy

- Follow SemVer:
  - Patch: fixes and internal improvements.
  - Minor: additive, backward-compatible features.
  - Major: breaking changes only.
- For potential breaks:
  - Prefer additive alternatives first.
  - Use `[Obsolete("message", error: false)]` for at least one minor release before removal.
  - Note changes in the release notes and update README examples as needed.

### Contribution workflow and CI

- Branching: create feature branches from `main` with concise names, e.g., `feat/query-projection`, `fix/deserialization-nullable`.
- Commits: use clear, conventional messages (e.g., `feat: add SelectMany support for references`, `fix: guard null token in SanityOptions`).
- PRs: include a summary, rationale, and tests. Call out any potential behavioral changes.
- CI: GitHub Actions workflow (`.github/workflows/dotnet.yml`) builds and runs tests on PRs. Ensure it passes locally with `dotnet build` and `dotnet test` before pushing.

---

If something is unclear or you have suggestions to improve this guide, please open an issue or a PR.
