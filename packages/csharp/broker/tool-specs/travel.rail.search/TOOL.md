# travel.rail.search

Purpose: broker-mediated Taiwan Railways timetable and ticket lookup.

Current status: active.

Rules:

- sources must be declared in source policy

- this tool is for TRA / 台鐵 / 火車 lookups, not THSR / 高鐵

- schedule results must be treated as time-sensitive

- responses must identify source and retrieval time

- returned options are candidate schedules and may include detected time candidates from public timetable pages
