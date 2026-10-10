using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace JH.DataBinding.Tests
{
  public class PropertyAccessorChainTest
  {
    private class Leaf
    {
      public int Value { get; set; }
      public Vector3 Position { get; set; }
    }

    private class Model
    {
      public Leaf Child { get; set; } = new Leaf();
      public virtual int Value { get; set; }
      public long LongValue { get; set; }
      public string Text { get; set; }
      public int? OptionalValue { get; set; }
    }

    private class DerivedModel : Model
    {
      public override int Value => 17;
    }

    private class HiddenModel : Model
    {
      public new int Value => 23;
    }

    private struct StructModel
    {
      public int Value { get; set; }
    }

    private class StaticModel
    {
      public static int Value { get; set; }
    }

    private class ThrowingModel
    {
      public int Value
      {
        get => throw new InvalidOperationException("getter");
        set => throw new InvalidOperationException("setter");
      }
    }

    private class ViewModel : DataSourceBase<ViewModel>
    {
      public Vector3 Position { get; set; }
      public Vector3 OtherPosition { get; set; }
    }

    private static PropertyAccessorChain Getter(object source, string path) =>
      CachedReflectionOperations.Instance.ResolvePropertyAccessors(
        source, path, PathResolveOperation.GetValue);

    private static PropertyAccessorChain Setter(object target, string path) =>
      CachedReflectionOperations.Instance.ResolvePropertyAccessors(
        target, path, PathResolveOperation.SetValue);

    [Test]
    public void NestedReferencePathsUseDelegatesAndUpdateTheActualTarget()
    {
      var source = new Model { Child = new Leaf { Value = 42 } };
      var target = new Model();
      var getter = Getter(source, "Child.Value");
      var setter = Setter(target, "Child.Value");

      Assert.IsFalse(getter.UsesReflection);
      Assert.IsFalse(setter.UsesReflection);
      Assert.AreEqual(42, getter.GetValue(source));
      getter.GetTransferTo(setter)(source, target);
      Assert.AreEqual(42, target.Child.Value);
    }

    [Test]
    public void ChainsAndTransfersAreSharedWithoutCapturingBoundObjects()
    {
      var first = new Model { Value = 3 };
      var second = new Model { Value = 9 };
      var firstTarget = new Model();
      var secondTarget = new Model();
      var firstGetter = Getter(first, "Value");
      var secondGetter = Getter(second, "Value");
      var firstSetter = Setter(firstTarget, "Value");
      var secondSetter = Setter(secondTarget, "Value");

      Assert.AreSame(firstGetter, secondGetter);
      Assert.AreSame(firstSetter, secondSetter);
      var transfer = firstGetter.GetTransferTo(firstSetter);
      Assert.AreSame(transfer, secondGetter.GetTransferTo(secondSetter));
      transfer(first, firstTarget);
      transfer(second, secondTarget);
      Assert.AreEqual(3, firstTarget.Value);
      Assert.AreEqual(9, secondTarget.Value);
    }

    [Test]
    public void InheritedOverridesAndHiddenPropertiesKeepTheirResolvedMeaning()
    {
      var derived = new DerivedModel();
      var hidden = new HiddenModel();
      Assert.AreEqual(17, Getter(derived, "Value").GetValue(derived));
      Assert.AreEqual(23, Getter(hidden, "Value").GetValue(hidden));
      Assert.AreNotSame(Getter(derived, "Value"), Getter(hidden, "Value"));
    }

    [Test]
    public void NumericConversionsAndNullAssignmentsKeepReflectionSemantics()
    {
      var source = new Model { Value = 5 };
      var target = new Model { Value = 10, OptionalValue = 20, Text = "old" };
      Getter(source, "Value").GetTransferTo(Setter(target, "LongValue"))(source, target);
      Assert.AreEqual(5L, target.LongValue);
      Setter(target, "Value").SetValue(target, null);
      Setter(target, "OptionalValue").SetValue(target, null);
      Setter(target, "Text").SetValue(target, null);
      Assert.AreEqual(0, target.Value);
      Assert.IsNull(target.OptionalValue);
      Assert.IsNull(target.Text);
      Setter(target, "OptionalValue").SetValue(target, 11);
      Assert.AreEqual(11, target.OptionalValue);
    }

    [Test]
    public void StructReceiversFallBackAndMutateTheExistingBox()
    {
      object model = new StructModel { Value = 1 };
      var getter = Getter(model, "Value");
      var setter = Setter(model, "Value");
      Assert.IsTrue(getter.UsesReflection);
      Assert.IsTrue(setter.UsesReflection);
      setter.SetValue(model, 7);
      Assert.AreEqual(7, getter.GetValue(model));
      Assert.AreEqual(7, ((StructModel)model).Value);
    }

    [Test]
    public void StaticPropertiesUseDelegates()
    {
      var model = new StaticModel();
      var getter = Getter(model, "Value");
      var setter = Setter(model, "Value");
      Assert.IsFalse(getter.UsesReflection);
      Assert.IsFalse(setter.UsesReflection);
      setter.SetValue(model, 31);
      Assert.AreEqual(31, getter.GetValue(model));
    }

    [Test]
    public void PropertyExceptionsRemainWrappedInTargetInvocationException()
    {
      var model = new ThrowingModel();
      var getterError = Assert.Throws<TargetInvocationException>(
        () => Getter(model, "Value").GetValue(model));
      var setterError = Assert.Throws<TargetInvocationException>(
        () => Setter(model, "Value").SetValue(model, 1));
      Assert.IsInstanceOf<InvalidOperationException>(getterError.InnerException);
      Assert.IsInstanceOf<InvalidOperationException>(setterError.InnerException);
      Assert.AreEqual("getter", getterError.InnerException.Message);
      Assert.AreEqual("setter", setterError.InnerException.Message);
    }

    [Test]
    public void NullRootsAndIntermediateObjectsPreserveExistingBehavior()
    {
      var model = new Model { Value = 99, Child = null };
      Assert.IsNull(Getter(model, "Value").GetValue(null));
      Getter(model, "Value").GetTransferTo(Setter(model, "Value"))(null, model);
      Assert.AreEqual(0, model.Value);
      Assert.Throws<TargetException>(() => Getter(model, "Child.Value").GetValue(model));
      Assert.Throws<ArgumentNullException>(() => Setter(model, "Value").SetValue(null, 1));
      Assert.Throws<TargetException>(() => Getter(model, "Value").GetValue(new object()));
    }

    [Test]
    public void ViewsUseDelegateTransfersAndRebindWhenPathsChange()
    {
      var gameObject = new GameObject("Delegate view");
      try
      {
        var view = gameObject.AddComponent<View>();
        var binding = new ComponentPropertyBinding
        {
          SourcePath = "Position", TargetComponent = view.transform, TargetPath = "localPosition",
        };
        view.componentPropertyBindings = new[] { binding };
        var source = new ViewModel { Position = Vector3.right, OtherPosition = Vector3.up };
        view.DataSource = source;
        Assert.AreEqual(Vector3.right, view.transform.localPosition);
        source.NotifyChanges(value => value.Position = Vector3.forward);
        Assert.AreEqual(Vector3.forward, view.transform.localPosition);
        binding.SourcePath = "OtherPosition";
        binding.UpdateBinding();
        Assert.AreEqual(Vector3.up, view.transform.localPosition);
        binding.TargetPath = "localScale";
        binding.UpdateBinding();
        Assert.AreEqual(Vector3.up, view.transform.localScale);
        view.DataSource = new ViewModel { OtherPosition = Vector3.one * 2 };
        Assert.AreEqual(Vector3.one * 2, view.transform.localScale);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(gameObject);
      }
    }

    [Test]
    public void WarmVectorTransfersReportReflectionComparison()
    {
      const int iterations = 20000;
      var source = new Model { Child = new Leaf { Position = new Vector3(1, 2, 3) } };
      var target = new Model();
      var getter = Getter(source, "Child.Position");
      var setter = Setter(target, "Child.Position");
      var transfer = getter.GetTransferTo(setter);
      Assert.IsFalse(getter.UsesReflection);
      Assert.IsFalse(setter.UsesReflection);
      var reflection = CachedReflectionOperations.Instance;
      var getterMethods = reflection.ResolvePublicPropertyPath(
        source, "Child.Position", PathResolveOperation.GetValue);
      var setterMethods = reflection.ResolvePublicPropertyPath(
        target, "Child.Position", PathResolveOperation.SetValue);
      for (var i = 0; i < 100; ++i)
      {
        transfer(source, target);
        IReflectionOperations.SyncValue(reflection, source, target, getterMethods, setterMethods);
      }

      var timer = new Stopwatch();
      timer.Start();
      for (var i = 0; i < iterations; ++i) transfer(source, target);
      timer.Stop();
      var delegateMilliseconds = timer.Elapsed.TotalMilliseconds;
      timer.Reset();
      timer.Start();
      for (var i = 0; i < iterations; ++i)
        IReflectionOperations.SyncValue(reflection, source, target, getterMethods, setterMethods);
      timer.Stop();

      TestContext.WriteLine($"{iterations} nested Vector3 transfers: delegates " +
        $"{delegateMilliseconds:F3} ms; reflection {timer.Elapsed.TotalMilliseconds:F3} ms.");
      Assert.AreEqual(source.Child.Position, target.Child.Position);
    }
  }
}
