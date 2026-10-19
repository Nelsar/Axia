# syntax=docker/dockerfile:1

# The console, built and served by nginx, which also proxies /api to the API. One origin for both
# means the browser makes no cross-origin calls, so Cors:AllowedOrigins can stay empty.

# What it builds is static files, the same on every platform, so it builds once, natively, and
# only the nginx stage below is per platform.
FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src

# Dependencies first, so `npm ci` is cached unless package-lock.json changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Talk to the origin the console was loaded from: the proxy below puts the API there.
ENV VITE_API_SAME_ORIGIN=true
RUN npm run build

# The unprivileged variant listens on 8080 and runs as a non-root user.
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY --from=build /src/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
