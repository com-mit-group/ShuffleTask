# ShuffleTask

## Prioritization formula

ShuffleTask ranks tasks by combining weighted importance, urgency, and a size-aware multiplier:

- **Importance** contributes up to 60 points based on a 1–5 rating.
- **Urgency** contributes up to 40 points and splits into deadline and repeat components. Deadline urgency now uses a size-aware window:
  - `windowHours = clamp(72 * (storyPoints / 3), 24, 168)`
  - Upcoming deadlines receive `deadlineUrgency = 1 - clamp(hoursUntilDeadline / windowHours, 0, 1)` while overdue work keeps the existing boost.
  - Repeat urgency is unchanged, still weighted at 25% of the urgency share with a streak penalty.
- **Size multiplier** nudges smaller wins upward while keeping larger efforts visible:
  - `sizeMultiplier = clamp(1 + 0.2 * (1 - storyPoints / 3), 0.8, 1.2)`
  - The final score is `(importancePoints + deadlinePoints + repeatPoints) * sizeMultiplier`.

Story point estimates default to 3 and can be adjusted between 0.5 and 13 in the task editor. Smaller estimates start boosting urgency closer to the deadline, while larger estimates surface earlier in the shuffle.

### Tune the balance

Open **Settings → Weighting** to tailor how the shuffle behaves:

- Adjust the **importance** and **urgency** point pools (defaults remain the 60/40 split described above).
- Split the urgency pool between **deadlines** and **repeating work** with a single slider.
- Control the **repeat penalty** to soften or remove the dampening on routine tasks.
- Dial the **size bias strength** down to zero to make scores size-agnostic or up to favor quick wins.

Changes are saved to your profile and take effect immediately in the next shuffle preview.
