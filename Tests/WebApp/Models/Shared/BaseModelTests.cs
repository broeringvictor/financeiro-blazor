using WebApp.Models.Shared;

namespace Tests.WebApp.Models.Shared;

public class BaseModelTests
{
    // Subclasse de teste que expõe os membros protegidos de BaseModel.
    private sealed class FakeModel : BaseModel
    {
        public void ConfigureForEditPublic(Guid id, DateTime createdAt) => ConfigureForEdit(id, createdAt);
        public void MarkAsUpdatedPublic() => MarkAsUpdated();
    }

    // ---------- Estado inicial ----------

    [Fact]
    public void Novo_DeveGerarIdVersion7()
    {
        var model = new FakeModel();

        Assert.NotEqual(Guid.Empty, model.Id);
        Assert.Equal(7, model.Id.Version);
    }

    [Fact]
    public void Novo_DeveDefinirDatasEManterDeletedAtNulo()
    {
        var antes = DateTime.UtcNow;

        var model = new FakeModel();

        Assert.InRange(model.CreatedAt, antes, DateTime.UtcNow);
        Assert.InRange(model.UpdatedAt, antes, DateTime.UtcNow);
        Assert.Null(model.DeletedAt);
    }

    [Fact]
    public void Novo_CadaInstancia_DeveTerIdUnico()
    {
        var a = new FakeModel();
        var b = new FakeModel();

        Assert.NotEqual(a.Id, b.Id);
    }

    // ---------- ConfigureForEdit ----------

    [Fact]
    public void ConfigureForEdit_DevePreservarIdECreatedAt()
    {
        var model = new FakeModel();
        var id = Guid.CreateVersion7();
        var createdAt = DateTime.UtcNow.AddDays(-3);

        model.ConfigureForEditPublic(id, createdAt);

        Assert.Equal(id, model.Id);
        Assert.Equal(createdAt, model.CreatedAt);
    }

    [Fact]
    public void ConfigureForEdit_DeveAtualizarUpdatedAtParaAgora()
    {
        var model = new FakeModel();
        var createdAt = DateTime.UtcNow.AddDays(-3);
        var antes = DateTime.UtcNow;

        model.ConfigureForEditPublic(Guid.CreateVersion7(), createdAt);

        Assert.InRange(model.UpdatedAt, antes, DateTime.UtcNow);
    }

    // ---------- MarkAsUpdated ----------

    [Fact]
    public void MarkAsUpdated_DeveAvancarUpdatedAtSemAfetarOsDemais()
    {
        var model = new FakeModel();
        var id = model.Id;
        var createdAt = model.CreatedAt;
        var updatedAtAnterior = model.UpdatedAt;

        model.MarkAsUpdatedPublic();

        Assert.True(model.UpdatedAt >= updatedAtAnterior);
        Assert.Equal(id, model.Id);
        Assert.Equal(createdAt, model.CreatedAt);
        Assert.Null(model.DeletedAt);
    }
}
