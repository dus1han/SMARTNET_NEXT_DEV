# @smartnet/api-client

The TypeScript types for the API, **generated from its OpenAPI schema**.

## Do not hand-edit anything here

`openapi.json` and `schema.d.ts` are both generated. An edit to either survives exactly until the
next `npm run generate:api`, and in the meantime it is a lie the compiler believes.

The reason this package exists at all is that hand-written types do not fail — they *drift*. Add a
field to a C# DTO and forget the frontend, and TypeScript keeps compiling happily, because it has
never heard of the field. The bug surfaces later, as a value that is silently `undefined`.

## Regenerating

From `apps/web`:

```bash
npm run generate:api
```

That rebuilds the API, emits `openapi.json` from the compiled assembly, and regenerates
`schema.d.ts`. It needs no database and no real secrets — the schema is derived from the code, not
from a running server.

## Keeping it honest

`npm run check:api` regenerates into a temporary file and fails if the result differs from what is
committed. It runs in CI, because a generated file that nobody regenerates is a hand-written file
with extra steps.

If CI fails on it: run `npm run generate:api` and commit the result.
