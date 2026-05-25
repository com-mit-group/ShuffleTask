namespace ShuffleTask.Persistence;

public interface IStorageFaultInjector
{
    void BeforeCommit(string operation);
}
