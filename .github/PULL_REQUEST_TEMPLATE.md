### PR Title format (Conventional Commits)

Use: `type(scope?): subject`

Examples:
- `feat: add LINQ projection for references`
- `fix(query): handle null filter edge case`
- `refactor(core): simplify mutation builder`
- `docs: update README with usage examples`
- `chore(ci): bump GitVersion action to 5.x`

Allowed types:
`feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `chore`, `revert`

Notes:
- `type` is lowercase.
- `scope` is optional, in parentheses.
- Use a concise, imperative `subject` after a colon and a space.

---

### Summary

Describe the motivation and high-level changes.

### Changes
- 

### Tests
- 

### Checklist
- [ ] PR title follows Conventional Commits
- [ ] Unit/integration tests added or updated as needed
- [ ] Docs updated (if applicable)