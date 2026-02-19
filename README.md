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
This provider follows EF Core’s standard extension model and is organized into three layers.

The first layer is EF Core itself. EF Core owns change tracking, identity resolution, query compilation, and navigation fix-up. The provider does not replace these mechanisms. It integrates with them.

The second layer is the provider integration layer. It connects EF Core’s pipeline to the storage runtime. This includes:
- The write path (CustomMemoryEfDatabase), which receives state changes during SaveChanges
- The translation layer, which records LINQ operations into CustomMemoryQueryExpression
- The compilation layer, which builds executable expression trees and reuses EF Core’s tracking infrastructure

The third layer is the in-memory runtime. It consists of:
- MemoryDatabaseRoot for database name isolation
- MemoryDatabase for table coordination and transactions
- MemoryTable<T> for snapshot-based storage

The storage layer is row-based and does not store entity instances directly. Entities are materialized during query execution and tracked by EF Core.

In short, EF Core manages tracking and identity. The provider manages translation and execution. The runtime manages snapshot storage.

### Supported Features
This provider implements a minimal but functional subset of EF Core to demonstrate how the pipeline works. 
It supports basic write operations (Add, Update, Remove, SaveChanges) and integrates fully with EF Core’s change tracking and identity resolution. 
On the query side, it supports common LINQ operators such as Where, Select, OrderBy, ThenBy, Skip, and Take, along with terminal operations like ToList, First, Single, Count, and Any.
Reference and collection Include are supported using a simplified in-memory strategy. Navigation fix-up and identity resolution are handled by EF Core’s tracking system.
Transactions are implemented using a simple table-level copy-on-write model.
Advanced LINQ operators, relational features, and performance optimizations are intentionally out of scope.

### Implement Process
 #### 1. [Registration and Discovery](https://dev.to/alexleeeeeeeeee/c-learning-notes-custom-in-memory-provider1-registration-and-discovery-12i0)
 #### 2. [In-Memory Database Runtime](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider2-in-memory-database-runtime-254p)
 #### 3. [Storage Write Model and Key-Based Retrieval](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider3-storage-write-model-and-key-based-retrieval-1i7m)
 #### 4. [ReadPath - From IQueryable to Result Execution](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider4-readpath-from-iqueryable-to-result-execution-f9l)
 #### 5. [Include & ThenInclude — Navigation Loading and Fix-Up](https://dev.to/alexleeeeeeeeee/net-learning-notes-custom-in-memory-provider5-include-theninclude-navigation-loading-and-13ga)

### Testing Strategy
At the storage layer, the in-memory database is validated using xUnit-based unit tests. These tests focus on snapshot persistence, change merging, and transaction isolation. The goal is to ensure deterministic behavior independent of EF Core’s higher-level pipeline.

At the provider layer, development follows a lightweight Test-Driven Development (TDD) approach. Smoke tests are defined first to describe expected EF Core behaviors—CRUD operations, basic LINQ queries, and Include scenarios. Provider features are then implemented incrementally until these behavioral contracts pass. This ensures integration correctness against EF Core’s translation, compilation, and tracking mechanisms.

### Limitations
This provider intentionally implements a minimal subset of EF Core behavior. Advanced LINQ operators such as complex joins, GroupBy, and deeply nested subqueries are not fully supported.

The transaction model is simplified to a single-transaction, table-level copy-on-write strategy without concurrency control or isolation levels.

Include support for collection navigations is implemented via expression rewriting and LINQ-to-Objects execution rather than full integration with EF Core’s relational include pipeline.

The storage layer is snapshot-based and supports scalar properties only. Complex object graphs, owned types, and advanced mapping scenarios are outside the current scope.

This project prioritizes architectural clarity over feature completeness and performance optimization.
