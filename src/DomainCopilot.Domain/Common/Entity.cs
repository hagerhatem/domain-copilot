namespace DomainCopilot.Domain.Common;

/// <summary>
/// Base type for all domain entities. Entities are compared by identity
/// (<see cref="Id"/>), not by the values of their properties — two entities with the
/// same <see cref="Id"/> are the same conceptual object even if their in-memory state
/// has diverged.
///
/// Invariants:
/// - <see cref="Id"/> is set once, at construction, and never changes.
/// - A domain-facing constructor (<see cref="Entity(Guid)"/>) always requires a
///   non-empty id; the parameterless constructor exists solely so an ORM can
///   materialize an instance via reflection and populate backing fields itself — it is
///   intentionally not framework-typed, so this file has zero dependency on EF Core or
///   any other persistence technology.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    /// <summary>Reserved for ORM materialization. Do not call from domain code.</summary>
    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Entity id cannot be empty.", nameof(id));

        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;

        return Id == other.Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);
    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
