# Architecture decision records

ADRs record decisions that constrain future architecture, data ownership, dependencies, compatibility, or operations. Small implementation choices that are easy to reverse do not need an ADR.

## Index

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-use-rabbitmq-client.md) | Accepted | Use the official RabbitMQ .NET client behind local abstractions |
| [0002](0002-use-modular-monorepo-and-schema-ownership.md) | Accepted | Use a modular monorepo with explicit schema ownership |
| [0003](0003-build-synchronous-slice-before-messaging.md) | Accepted | Prove the synchronous trading slice before messaging |
| [0004](0004-use-leased-outbox-and-explicit-recovery.md) | Accepted | Use leased outbox dispatch and explicit messaging recovery |

## Format

Use the next four-digit number and a short kebab-case filename. Include:

- title, status, and date;
- context and forces;
- decision;
- consequences, including costs and rejected alternatives;
- supersession links when replacing an accepted decision.

Accepted ADRs are historical records. Correct typos or broken links in place, but capture a changed decision in a new ADR and mark the old record superseded.
