# Browser Reference: User Delegated Read

Status: `planned`

Purpose:

- document a browser capability that authenticates as a user under explicit broker-mediated delegation

Identity mode:

- `user_delegated`

Rules:

- credentials or session grants belong to the user
- the broker mediates access and binds the session to that user
- audit and consent requirements are the strictest of the three initial browser identity classes

This is a reference spec.
It is not active until delegated session, consent, and browser-worker runtime support are implemented.
