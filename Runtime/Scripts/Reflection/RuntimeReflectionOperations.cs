using RuntimeReflectionImplementation = JH.DataBinding.CachedReflectionOperations;

namespace JH.DataBinding
{
  public static class RuntimeReflectionOperations
  {
    // Change the alias above to select the runtime implementation without boxing.
    public static readonly RuntimeReflectionImplementation Instance = RuntimeReflectionImplementation.Instance;
  }
}
