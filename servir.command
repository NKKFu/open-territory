#!/bin/zsh
cd "${0:A:h}"
GAME_PORT=8790
while lsof -nP -iTCP:${GAME_PORT} -sTCP:LISTEN >/dev/null 2>&1; do
  GAME_PORT=$((GAME_PORT + 1))
done
python3 -m http.server "$GAME_PORT" --bind 127.0.0.1 &
GAME_SERVER_PID=$!
trap 'kill "$GAME_SERVER_PID" 2>/dev/null' EXIT INT TERM
for attempt in {1..50}; do
  if curl -fsS "http://127.0.0.1:${GAME_PORT}/" >/dev/null 2>&1; then break; fi
  sleep 0.1
done
open "http://127.0.0.1:${GAME_PORT}/"
wait "$GAME_SERVER_PID"
