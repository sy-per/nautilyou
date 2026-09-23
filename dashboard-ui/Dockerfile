# Image "web" : construit le dashboard (Vite) puis le sert avec nginx, qui sert aussi de reverse proxy
# HTTPS devant le serveur Nautilyou (/api et /ws/device).

FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# API sur la meme origine que le dashboard (nginx relaie /api vers le serveur).
ENV VITE_API_URL=/api
RUN npm run build

FROM nginx:1.27-alpine
RUN apk add --no-cache openssl
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/40-selfsigned-cert.sh /docker-entrypoint.d/40-selfsigned-cert.sh
RUN chmod +x /docker-entrypoint.d/40-selfsigned-cert.sh
VOLUME ["/certs"]
EXPOSE 80 443
