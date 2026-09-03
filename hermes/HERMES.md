# Hermes multi-agent travel demo

The only input is `data/travel-request.txt`. The final output is
`hermes/artifacts/weekend-plan.md`.

## Agent hierarchy

The primary Hermes agent creates one **Program coordinator** subagent with
`role: orchestrator`. The coordinator must create exactly one parallel batch
of three leaf agents:

1. **Route planner**
   - Proposes realistic transport and route variants.
   - Checks travel durations, opening windows, and geographic ordering.
   - Provides a primary route and a practical fallback.
2. **Meteorologist**
   - Uses current web sources to find the forecast for the requested dates and
     places.
   - Reports rain, temperature, wind, forecast uncertainty, source URLs, and
     retrieval time.
   - Suggests indoor alternatives when outdoor activities are risky.
3. **Budget analyst**
   - Estimates transport, accommodation, food, admissions, parking, and a
     contingency reserve in CZK.
   - Distinguishes known prices from estimates and rejects plans over budget.
4. **Program coordinator**
   - Receives all three summaries, resolves conflicts, and creates one feasible
     itinerary.
   - Adjusts activities according to weather, available time, and budget.
   - Keeps the trip within hard constraints from the request.

## Coordination rules

- Child agents start without conversation history. Every delegation must
  include the absolute project path, the complete request, constraints, and
  the expected output.
- The coordinator must wait for all three specialists before synthesizing.
- Priority order for conflicts:
  1. safety and explicit hard constraints,
  2. feasible timing,
  3. total budget,
  4. preferences and optional activities.
- Weather data must never be invented. Missing or uncertain forecasts must be
  labelled and handled with a conditional plan.
- Costs must include a 10% contingency reserve and show the total per trip and
  per person.
- Agents may read inputs and web sources but must not edit `data/`.
- The primary Hermes agent writes only the coordinator's final result to
  `hermes/artifacts/weekend-plan.md`.
