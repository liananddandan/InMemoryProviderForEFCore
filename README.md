### Project Overview
This project builds a custom in-memory EF Core provider from scratch to explore and demonstrate the EF Core architecture. It covers core components such as service registration, query translation and compilation,  entity tracking, identity resolution, include/then-include handling, and transaction isolation.

Unlike the built-in InMemory provider, this implementation explicitly models EF Core’s internal pipeline — from IDbContextOptionsExtension registration, to custom query expressions, shaped query compilation, snapshot-based storage, and navigation fix-up — in order to expose and control each stage of execution.

The goal of the project is to showcase architectural clarity, provider extensibility knowledge, and the ability to analyze and implement complex framework internals. It is intended as both a  technical showcase and a knowledge-sharing project.
### Motivation
### Architecture Overview
### Supported Features
### Implement Process
 #### 1. [Registration and Discovery](https://dev.to/alexleeeeeeeeee/c-learning-notes-custom-in-memory-provider1-registration-and-discovery-12i0)

### Testing Strategy
### Limitations
