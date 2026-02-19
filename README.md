### Project Overview
This project builds a custom in-memory EF Core provider from scratch to explore and demonstrate the EF Core architecture. It covers core components such as service registration, query translation and compilation,  entity tracking, identity resolution, include/then-include handling, and transaction isolation.

Unlike the built-in InStorage Memory provider, this implementation explicitly models EF Core’s internal pipeline — from IDbContextOptionsExtension registration, to custom query expressions, shaped query compilation, snapshot-based storage, and navigation fix-up — in order to expose and control each stage of execution.

The goal of the project is to showcase architectural clarity, provider extensibility knowledge, and the ability to analyze and implement complex framework internals. It is intended as both a  technical showcase and a knowledge-sharing project.

### Motivation
This project was built with four goals.

First, to understand EF Core from the inside. Instead of using it only as an ORM, I wanted to explore how its query pipeline, tracking system, and provider model actually work.

Second, to make EF Core’s internal architecture more transparent and accessible by open-sourcing a minimal custom provider implementation, allowing others to study, experiment with, and reason about how EF Core actually works under the hood.

Third, to experiment with AI-assisted development on non-trivial infrastructure code, especially around expression trees and query compilation.

Finally, to deepen my understanding of advanced C# and .NET concepts such as generics, reflection, metadata, and runtime expression compilation.

### Architecture Overview
### Supported Features
### Implement Process
 #### 1. [Registration and Discovery](https://dev.to/alexleeeeeeeeee/c-learning-notes-custom-in-memory-provider1-registration-and-discovery-12i0)
 #### 2. [In-Memory Database Runtime](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider2-in-memory-database-runtime-254p)
 #### 3. [Storage Write Model and Key-Based Retrieval](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider3-storage-write-model-and-key-based-retrieval-1i7m)
 #### 4. [ReadPath - From IQueryable to Result Execution](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider4-readpath-from-iqueryable-to-result-execution-f9l)
 #### 5. [Include & ThenInclude — Navigation Loading and Fix-Up](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider5-include-theninclude-navigation-loading-and-13ga)
### Testing Strategy
### Limitations
