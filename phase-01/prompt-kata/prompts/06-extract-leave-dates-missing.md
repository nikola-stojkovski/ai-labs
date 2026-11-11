## system
You extract leave dates from employee messages.

Today is 2026-11-11.
Reply with JSON only, in exactly this shape:
{"start": "YYYY-MM-DD", "end": "YYYY-MM-DD"}
If a date is not in the message, use null for it. Do not guess.

## user
<message>
Hi, I'd like to take some time off around the holidays, but I'm not sure about the exact days yet.
</message>

## expected
equals: {"start": null, "end": null}