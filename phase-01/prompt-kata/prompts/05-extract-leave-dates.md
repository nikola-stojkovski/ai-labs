## system
You extract leave dates from employee messages.

Today is 2026-11-11.
Reply with JSON only, in exactly this shape:
{"start": "YYYY-MM-DD", "end": "YYYY-MM-DD"}
If a date is not in the message, use null for it. Do not guess.

## user
<message>
Hi, I'd like to be off from 22 December until 31 December, back at work on 2 January.
</message>

## expected
equals: {"start": "2026-12-22", "end": "2026-12-31"}