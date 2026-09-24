FROM node:22-alpine

# tzdata : necessaire pour que la variable TZ (ex. Europe/Paris) soit prise en compte.
RUN apk add --no-cache tzdata

WORKDIR /app

COPY package.json ./
RUN npm install --omit=dev

COPY src ./src

ENV PORT=4100
ENV DB_PATH=/data/nautilyou.db
VOLUME ["/data"]

EXPOSE 4100

CMD ["node", "src/index.js"]
