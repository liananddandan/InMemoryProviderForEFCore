using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomEFCoreProvider.Samples.Entities.Config;

public class TestEntityConfiguration : IEntityTypeConfiguration<TestEntity>
{
    public void Configure(EntityTypeBuilder<TestEntity> builder)
    {
        builder.ToTable("T_TestEntities");
        builder.HasKey(x => x.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();
    }
}