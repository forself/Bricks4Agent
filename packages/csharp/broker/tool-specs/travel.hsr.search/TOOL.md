# travel.hsr.search

Purpose: broker-mediated Taiwan High Speed Rail timetable lookup.

Current status: active.

Rules:

- sources must be declared in source policy
- this tool is for THSR / 高鐵 lookups, not TRA / 台鐵 / 火車
- schedule results must be treated as time-sensitive
- responses must identify source and retrieval time
- returned options are candidate schedules and may include detected time candidates from public timetable pages
