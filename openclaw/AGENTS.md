# OpenClaw multi-agent travel demo

The only input is `data/travel-request.txt`. The final output is
`openclaw/artifacts/weekend-plan.md`.

## Agent hierarchy

The primary OpenClaw agent creates exactly one **Program coordinator**
sub-agent. The coordinator creates exactly three leaf sub-agents concurrently:

1. **Route planner** (`taskName: route_planner`)
   - Proposes realistic transport and route variants.
   - Checks travel durations, opening windows, and geographic ordering.
   - Provides a primary route and a practical fallback.
2. **Meteorologist** (`taskName: meteorologist`)
   - Uses current web sources to find the forecast for the requested dates and
     places.
   - Reports rain, temperature, wind, forecast uncertainty, source URLs, and
     retrieval time.
   - Suggests indoor alternatives when outdoor activities are risky.
3. **Budget analyst** (`taskName: budget_analyst`)
   - Estimates transport, accommodation, food, admissions, parking, and a
     contingency reserve in CZK.
   - Distinguishes known prices from estimates and rejects plans over budget.
4. **Program coordinator** (`taskName: program_coordinator`)
   - Receives all three summaries, resolves conflicts, and creates one feasible
     itinerary.
   - Adjusts activities according to weather, available time, and budget.
   - Keeps the trip within hard constraints from the request.

## Coordination rules

- Use native OpenClaw `sessions_spawn` calls with `runtime: "subagent"` and
  `context: "isolated"`.
- Child agents start without conversation history. Every delegation must
  include the complete request, constraints, role, and expected output.
- Never pass `cwd` to `sessions_spawn`; every child must inherit the configured
  repository workspace.
- The coordinator must issue all three leaf `sessions_spawn` calls in one turn,
  without a `cwd` parameter, then call `sessions_yield`. It must not poll child
  status.
- The primary agent must call `sessions_yield` after starting the coordinator.
- Priority order for conflicts:
  1. safety and explicit hard constraints,
  2. feasible timing,
  3. total budget,
  4. preferences and optional activities.
- Weather data must never be invented. Missing or uncertain forecasts must be
  labelled and handled with a conditional plan. The final plan must preserve at
  least one complete `https://` source URL supplied by the meteorologist.
- Costs must include a 10% contingency reserve and show the total per trip and
  per person.
- Agents may read inputs and web sources but must not edit `data/`.
- Agents must not create scratch or bootstrap files in the repository. Web
  research must use `web_search` or `web_fetch`, not downloaded temporary files.
- The primary agent writes only the coordinator's final result to
  `openclaw/artifacts/weekend-plan.md`.
