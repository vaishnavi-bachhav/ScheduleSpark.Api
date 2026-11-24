# ScheduleSpark API

Backend foundation for Team D's course scheduling assistant.

## Run locally

Install .NET 8 SDK and MongoDB, then run:

```bash
dotnet restore
dotnet run
```

Open Swagger at `http://localhost:5000/swagger` or use the port printed by ASP.NET Core.

## Run with Docker

```bash
docker compose up --build
```

Swagger: `http://localhost:8080/swagger`

## Initial endpoints

- `GET /health`
- `GET /api/planning/workspace/{semester}`
- `POST /api/planning/offerings`
- `POST /api/schedules/generate`
- `POST /api/schedules/validate`
- `GET /api/schedules/drafts`
- `GET /api/schedules/drafts/{id}`
- `POST /api/schedules/drafts`

The University at Albany catalog is treated as reference data. Semester offerings and scheduling resources are managed through the planning workflow.
