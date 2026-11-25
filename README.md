# ScheduleSpark backend

ASP.NET Core Web API for semester planning, course scheduling, assignment validation, imports, and saved draft history. Data is stored in MongoDB.

## Requirements

- .NET 10 SDK for local development (see `ScheduleSpark.Api.csproj`)
- MongoDB 8, either installed locally or started with Docker Compose
- Docker Desktop / Docker Engine for the containerized setup

## Quick start with Docker Compose

From this directory (`ScheduleSpark/ScheduleSpark.Api`):

```sh
docker compose up --build
```

This starts MongoDB and the API. MongoDB is exposed on port `27017`; the API is exposed at `http://localhost:8080`.

- Swagger UI: <http://localhost:8080/swagger>
- Health check: <http://localhost:8080/health>

Stop the services with `Ctrl+C`, or run `docker compose down` in another terminal. The named `schedulespark-data` volume keeps MongoDB data when containers stop. Removing that volume with `docker compose down -v` permanently deletes the saved local database.

Configure the frontend to use this API by setting `VITE_API_BASE_URL=http://localhost:8080` in `course_scheduler/.env.local`, then restart the frontend dev server. See the [frontend README](../../course_scheduler/README.md).

## Run locally without Docker

Start a MongoDB server on `localhost:27017`, then from this directory run:

```sh
dotnet restore
dotnet run
```

The checked-in .NET launch profile listens on `https://localhost:52141` and `http://localhost:52142`. Open Swagger at <http://localhost:52142/swagger> or <https://localhost:52141/swagger>. If using the HTTPS URL from the frontend, trust the local ASP.NET Core development certificate or use the HTTP URL instead.

The default database settings are:

| Setting | Default |
| --- | --- |
| MongoDB connection | `mongodb://localhost:27017` |
| Database name | `schedulespark` |

Override settings with ASP.NET Core configuration environment variables when needed:

```text
MongoDb__ConnectionString=mongodb://localhost:27017
MongoDb__DatabaseName=schedulespark
```

For a fixed local HTTP port instead of the launch profile, set `ASPNETCORE_URLS` to `http://localhost:8080` and run without the profile. For example, in PowerShell:

```powershell
$env:ASPNETCORE_URLS = 'http://localhost:8080'
dotnet run --no-launch-profile
```

In that setup, use `http://localhost:8080` as `VITE_API_BASE_URL` in the frontend.

## Build

```sh
dotnet build ScheduleSpark.sln
```

## Startup data

At startup, `DatabaseSeeder` adds example Fall 2026 offerings, instructors, and rooms when those collections are empty. It also adds missing default scheduling constraints and migrates two legacy constraint names. It does not overwrite non-empty course, instructor, or room collections. Approved schedule blocks are managed separately; configure them in the frontend under **Planning Workspace → University Blocks** before saving instructor availability or generating a schedule.

The sample data is for development. Keep production connection strings and credentials outside source control, and use a dedicated MongoDB database for production.

## API overview

All endpoints are available from Swagger at `/swagger`.

### Planning and imports

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/api/planning/workspace/{semester}` | Load semester offerings, resources, constraints, and block settings |
| `POST` | `/api/planning/offerings` | Add an offering |
| `PUT` | `/api/planning/blocks/{semester}` | Save approved scheduling blocks |
| `PUT` | `/api/planning/instructors/{instructorId}/availability/{semester}` | Save instructor availability |
| `POST` | `/api/planning/import/preview` | Validate uploaded CSV or Excel files without importing |
| `POST` | `/api/planning/import/confirm` | Confirm a validated import batch |

### Instructors, rooms, and constraints

Resources use the `/api` prefix:

- `/api/instructors` — list, create, retrieve, update, and delete instructors.
- `/api/rooms` — list, create, retrieve, update, and delete rooms.
- `/api/constraints` — list, create, update, and delete scheduling constraints.

Use `GET`, `POST`, and `PUT` on each collection or resource as appropriate; `DELETE /api/{instructors|rooms|constraints}/{id}` removes a resource.

### Schedules and drafts

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/schedules/generate` | Generate a draft from the supplied offering IDs |
| `POST` | `/api/schedules/validate` | Validate draft assignments and return conflicts |
| `PUT` | `/api/schedules/drafts/{draftId}/sections/{courseCode}` | Edit a course assignment in a draft |
| `GET` | `/api/schedules/drafts` | List saved drafts |
| `GET` | `/api/schedules/drafts/{id}` | Retrieve a saved draft |
| `POST` | `/api/schedules/drafts` | Save a draft |
| `DELETE` | `/api/schedules/drafts/{id}` | Delete a draft |
| `POST` | `/api/schedules/drafts/{id}/duplicate` | Duplicate a draft |
| `GET` | `/api/schedules/drafts/{id}/versions` | List draft versions |
| `POST` | `/api/schedules/drafts/{id}/versions/{versionNumber}/restore` | Restore a saved version |

### Health

`GET /health` returns a small JSON status response to confirm that the API process is running. Swagger is available at `/swagger` in the current application configuration.

For example, generate a draft by sending `POST /api/schedules/generate` with JSON shaped like this (replace the IDs with offering IDs returned by the workspace endpoint):

```json
{
  "semester": "Fall 2026",
  "offeringIds": ["offering-id-1", "offering-id-2"]
}
```

## Troubleshooting

- **MongoDB connection failure on startup:** confirm MongoDB is listening at the configured connection string. With Compose, check `docker compose ps` and the API logs with `docker compose logs api`.
- **Frontend cannot reach the API:** set `VITE_API_BASE_URL` to the API origin, including the scheme and port, then restart Vite. The API currently allows requests from any origin; restrict CORS and configure appropriate authentication/authorization before exposing the service in production.
- **Availability says a block is disabled or unapproved:** enable and save that block for the selected semester in the Planning Workspace before saving availability.
- **Swagger is unavailable:** check the API's actual listening URL in its startup output or the configured launch profile.
