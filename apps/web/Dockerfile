# Smartnet web — Next.js standalone, Linux.
#
# **Built from the repository root, not from apps/web:**
#
#   docker build -f apps/web/Dockerfile .
#
# The app imports `@smartnet/api-client`, which tsconfig maps to ../../packages/api-client — outside
# apps/web. With apps/web as the build context that path is not in the image at all, and the Next build
# fails typechecking every file in src/lib. The context is therefore the repo root.
#
# The repo's directory shape is reproduced inside the image rather than flattened, so the ../../ in
# tsconfig resolves for the same reason it does on a developer's machine, and nobody has to work out
# why a path that reads "two levels up" landed somewhere else.
#
# Three stages, so editing a component does not reinstall npm: dependencies are their own layer and are
# invalidated only by package-lock.json.

FROM node:20-alpine AS deps
WORKDIR /src/apps/web
COPY apps/web/package.json apps/web/package-lock.json* ./
RUN npm ci

FROM node:20-alpine AS build
WORKDIR /src/apps/web
COPY --from=deps /src/apps/web/node_modules ./node_modules

# ../../packages/api-client from /src/apps/web is /src/packages/api-client — the same relative path the
# checkout has.
COPY packages/api-client /src/packages/api-client
COPY apps/web ./

RUN npm run build

FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

RUN addgroup -g 1001 -S nodejs && adduser -S nextjs -u 1001

COPY --from=build /src/apps/web/public ./public
COPY --from=build --chown=nextjs:nodejs /src/apps/web/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /src/apps/web/.next/static ./.next/static

USER nextjs
EXPOSE 3000
CMD ["node", "server.js"]
