# The Linux Player build (Game.x86_64, Game_Data/, UnityPlayer.so) is produced into build/ by
# .github/workflows/nightly-smoke.yml (SmokeHostBuild.BuildFromCommandLine) before this image is built in CI;
# locally, run `powershell -File tests/run-smoke.ps1` first. One image plays every role in docker-compose.yml: the
# dedicated server and both bot clients, picked at run time by the MINDATTIC_ROLE environment variable
# (tests/MinimalSample/Runtime/SmokeDriver.cs).
#
#   docker build -f smoke/Bot.Dockerfile -t mindattic-smoke-game:ci smoke
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home --shell /bin/false game
WORKDIR /game
COPY build/ ./
RUN chmod +x Game.x86_64 && mkdir -p /shared && chown -R game:game /game /shared
USER game
ENTRYPOINT ["./Game.x86_64"]
