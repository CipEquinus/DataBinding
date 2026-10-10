using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace JH.DataBinding.Tests
{
  public class ViewBindingPlanTest
  {
    private readonly List<GameObject> objects = new();

    private class Item : DataSourceBase<Item>
    {
      public virtual Vector3 Position { get; set; }
      public Vector3 OtherPosition { get; set; }
      public IEnumerable<INotifyDataSourceChanged> Items { get; set; }
    }

    private class DerivedItem : Item
    {
      public override Vector3 Position => Vector3.up * 7;
    }

    private class HiddenItem : Item
    {
      public new Vector3 Position => Vector3.right * 9;
    }

    private class Collection : DataSourceBase<Collection>
    {
      public IEnumerable<INotifyDataSourceChanged> Items { get; set; }
      public IEnumerable<INotifyDataSourceChanged> OtherItems { get; set; }
    }

    [SetUp]
    public void SetUp() => ClearGlobalPlans();

    [TearDown]
    public void TearDown()
    {
      // Destroy containers before templates so OnDestroy can clear their children.
      for (var i = objects.Count - 1; i >= 0; --i)
      {
        if (objects[i] != null)
        {
          UnityEngine.Object.DestroyImmediate(objects[i]);
        }
      }

      objects.Clear();
    }

    [Test]
    public void SeparateContainersShareOnePlanAndKeepTheirOwnTargets()
    {
      var template = CreateTemplate();
      var first = CreateContainer(template, new Item { Position = Vector3.right });
      var second = CreateContainer(template, new Item { Position = Vector3.up });
      var firstChild = Child(first);
      var secondChild = Child(second);

      Assert.AreSame(Plan(firstChild), Plan(secondChild));
      Assert.AreNotSame(firstChild.componentPropertyBindings[0], secondChild.componentPropertyBindings[0]);
      Assert.AreEqual(Vector3.right, firstChild.transform.localPosition);
      Assert.AreEqual(Vector3.up, secondChild.transform.localPosition);
      Assert.AreSame(firstChild.transform, firstChild.componentPropertyBindings[0].TargetComponent);
      Assert.AreSame(secondChild.transform, secondChild.componentPropertyBindings[0].TargetComponent);
    }

    [Test]
    public void IndependentTemplatesShareTheSameGlobalPlanWithoutResolvingSourcesAgain()
    {
      var firstTemplate = CreateTemplate();
      var secondTemplate = CreateTemplate();
      var first = CreateContainer(firstTemplate, new Item());
      var cache = PathCache();
      var key = (typeof(Item), nameof(Item.Position), PathResolveOperation.GetValue);
      cache.TryRemove(key, out var accessors);
      try
      {
        var second = CreateContainer(secondTemplate, new Item { Position = Vector3.up });
        Assert.AreNotSame(firstTemplate, secondTemplate);
        Assert.AreSame(Plan(Child(first)), Plan(Child(second)));
        Assert.IsFalse(cache.ContainsKey(key), "Another branch resolved an already prepared source path.");
        Assert.AreEqual(Vector3.up, Child(second).transform.localPosition);
      }
      finally
      {
        if (accessors != null) cache[key] = accessors;
      }
    }

    [Test]
    public void DirectViewAssignmentsUseTheSameGlobalPlanAsContainerChildren()
    {
      var template = CreateTemplate();
      var container = CreateContainer(template, new Item());
      var directView = CreateTemplate();
      directView.DataSource = new Item { Position = Vector3.up };

      Assert.AreSame(Plan(Child(container)), Plan(directView));
      Assert.AreEqual(Vector3.up, directView.transform.localPosition);
    }

    [Test]
    public void RebindingAndCreatingMoreChildrenDoNotResolveCachedPathsAgain()
    {
      var template = CreateTemplate();
      var container = CreateContainer(template, new Item());
      var originalPlan = Plan(Child(container));
      var cache = PathCache();
      var sourceKey = (typeof(Item), nameof(Item.Position), PathResolveOperation.GetValue);
      var targetKey = (typeof(Transform), "localPosition", PathResolveOperation.SetValue);
      cache.TryRemove(sourceKey, out var sourceAccessors);
      cache.TryRemove(targetKey, out var targetAccessors);

      try
      {
        Child(container).OnEnable();
        var replacements = new INotifyDataSourceChanged[100];
        for (var i = 0; i < replacements.Length; ++i)
        {
          replacements[i] = new Item { Position = Vector3.right * i };
        }

        ((Collection)container.DataSource).NotifyChanges(source => source.Items = replacements);

        Assert.IsFalse(cache.ContainsKey(sourceKey), "Source path was resolved again.");
        Assert.IsFalse(cache.ContainsKey(targetKey), "Target path was resolved again.");
        for (var i = 0; i < replacements.Length; ++i)
        {
          var child = Child(container, i);
          Assert.AreSame(originalPlan, Plan(child));
          Assert.AreEqual(Vector3.right * i, child.transform.localPosition);
        }
      }
      finally
      {
        if (sourceAccessors != null) cache[sourceKey] = sourceAccessors;
        if (targetAccessors != null) cache[targetKey] = targetAccessors;
      }
    }

    [Test]
    public void MixedTypesUseSeparatePlansAndRespectOverridesAndHiddenProperties()
    {
      var container = CreateContainer(CreateTemplate(),
        new Item { Position = Vector3.forward }, new DerivedItem(), new HiddenItem(), new DerivedItem());

      Assert.AreEqual(Vector3.forward, Child(container, 0).transform.localPosition);
      Assert.AreEqual(Vector3.up * 7, Child(container, 1).transform.localPosition);
      Assert.AreEqual(Vector3.right * 9, Child(container, 2).transform.localPosition);
      Assert.AreNotSame(Plan(Child(container, 0)), Plan(Child(container, 1)));
      Assert.AreSame(Plan(Child(container, 1)), Plan(Child(container, 3)));

      ((Collection)container.DataSource).NotifyChanges(source => source.Items = new[] { new HiddenItem() });
      Assert.AreEqual(Vector3.right * 9, Child(container).transform.localPosition);
    }

    [Test]
    public void ReplacingSourceDetachesOldNotificationsAndKeepsNewNotifications()
    {
      var oldItem = new Item { Position = Vector3.right };
      var container = CreateContainer(CreateTemplate(), oldItem);
      var child = Child(container);
      var newItem = new Item { Position = Vector3.up };
      ((Collection)container.DataSource).NotifyChanges(source => source.Items = new[] { newItem });

      // Bypass the property setter to detect an unexpected old-source callback.
      child.transform.localPosition = Vector3.zero;
      oldItem.NotifyChanges(source => source.Position = Vector3.forward);
      Assert.AreEqual(Vector3.zero, child.transform.localPosition);
      newItem.NotifyChanges(source => source.Position = Vector3.one);
      Assert.AreEqual(Vector3.one, child.transform.localPosition);
    }

    [Test]
    public void CustomizedChildUsesAPlanForItsOwnBindingDefinition()
    {
      var container = CreateContainer(CreateTemplate(), new Item());
      var child = Child(container);
      child.componentPropertyBindings[0].SourcePath = nameof(Item.OtherPosition);
      ((Collection)container.DataSource).NotifyChanges(source => source.Items = new[]
      {
        new Item { Position = Vector3.right, OtherPosition = Vector3.up },
      });

      Assert.AreNotSame(Plan(child), Plan(Child(CreateContainer(CreateTemplate(), new Item()))));
      Assert.AreEqual(Vector3.up, child.transform.localPosition);
    }

    [Test]
    public void TemplateEditsRebuildPlansForNewChildren()
    {
      var template = CreateTemplate();
      var first = CreateContainer(template, new Item());
      var oldPlan = Plan(Child(first));
      template.componentPropertyBindings[0].SourcePath = nameof(Item.OtherPosition);
      var second = CreateContainer(template,
        new Item { Position = Vector3.right, OtherPosition = Vector3.up });

      Assert.AreNotSame(oldPlan, Plan(Child(second)));
      Assert.AreEqual(Vector3.up, Child(second).transform.localPosition);
    }

    [Test]
    public void ReplacingChildBindingArraysPreservesItsIndependentConfiguration()
    {
      var item = new Item { Position = Vector3.right, OtherPosition = Vector3.up };
      var container = CreateContainer(CreateTemplate(), item);
      var child = Child(container);
      child.componentPropertyBindings = new[]
      {
        new ComponentPropertyBinding
        {
          SourcePath = nameof(Item.OtherPosition), TargetComponent = child.transform, TargetPath = "localPosition",
        },
      };
      ((Collection)container.DataSource).NotifyChanges(source => source.Items = new[] { item });

      Assert.AreNotSame(Plan(child), Plan(Child(CreateContainer(CreateTemplate(), new Item()))));
      Assert.AreEqual(Vector3.up, child.transform.localPosition);
    }

    [Test]
    public void InvalidatingAfterEditingArrayEntriesReinstallsTheCorrectAccessors()
    {
      var view = CreateTemplate();
      var item = new Item { Position = Vector3.right, OtherPosition = Vector3.up };
      view.DataSource = item;
      view.componentPropertyBindings[0] = new ComponentPropertyBinding
      {
        SourcePath = nameof(Item.OtherPosition), TargetComponent = view.transform, TargetPath = "localPosition",
      };
      view.InvalidateBindingPlans();
      view.DataSource = item;
      Assert.AreEqual(Vector3.up, view.transform.localPosition);
    }

    [Test]
    public void NestedContainersSharePlansEvenWhenTheirTemplatesAreCloned()
    {
      var template = CreateTemplate();
      var leafTemplate = CreateTemplate();
      leafTemplate.transform.SetParent(template.transform);
      var nestedTarget = new GameObject("Nested target");
      nestedTarget.transform.SetParent(template.transform);
      template.containerPropertyBindings = new[]
      {
        new ContainerPropertyBinding
        {
          SourcePath = nameof(Item.Items), TargetContainer = nestedTarget.transform, ElementTemplate = leafTemplate,
        },
      };
      var container = CreateContainer(template,
        new Item { Items = new[] { new Item { Position = Vector3.right } } },
        new Item { Items = new[] { new Item { Position = Vector3.up } } });
      var firstBinding = Child(container, 0).containerPropertyBindings[0];
      var secondBinding = Child(container, 1).containerPropertyBindings[0];
      var firstLeaf = firstBinding.TargetContainer.GetChild(0).GetComponent<View>();
      var secondLeaf = secondBinding.TargetContainer.GetChild(0).GetComponent<View>();

      Assert.AreNotSame(firstBinding.ElementTemplate, secondBinding.ElementTemplate);
      Assert.AreSame(Plan(firstLeaf), Plan(secondLeaf));
      Assert.AreEqual(Vector3.right, firstLeaf.transform.localPosition);
      Assert.AreEqual(Vector3.up, secondLeaf.transform.localPosition);
    }

    [Test]
    public void DeepIndependentBranchesSharePlansAtEveryLevel()
    {
      var first = CreateContainer(CreateNestedTemplate(3), CreateNestedItem(3, Vector3.right));
      var second = CreateContainer(CreateNestedTemplate(3), CreateNestedItem(3, Vector3.up));
      var firstView = Child(first);
      var secondView = Child(second);
      for (var depth = 0; depth < 3; ++depth)
      {
        Assert.AreSame(Plan(firstView), Plan(secondView), $"Different plans at depth {depth}.");
        firstView = firstView.containerPropertyBindings[0].TargetContainer.GetChild(0).GetComponent<View>();
        secondView = secondView.containerPropertyBindings[0].TargetContainer.GetChild(0).GetComponent<View>();
      }

      Assert.AreSame(Plan(firstView), Plan(secondView));
      Assert.AreEqual(Vector3.right, firstView.transform.localPosition);
      Assert.AreEqual(Vector3.up, secondView.transform.localPosition);
    }

    [Test]
    public void DifferentTargetPathsAndBindingOrdersDoNotSharePlans()
    {
      var positionTemplate = CreateTemplate();
      var scaleTemplate = CreateTemplate();
      scaleTemplate.componentPropertyBindings[0].TargetPath = "localScale";
      var first = CreateContainer(positionTemplate, new Item { Position = Vector3.up });
      var second = CreateContainer(scaleTemplate, new Item { Position = Vector3.one * 2 });
      Assert.AreNotSame(Plan(Child(first)), Plan(Child(second)));
      Assert.AreEqual(Vector3.one * 2, Child(second).transform.localScale);

      var ordered = CreateTemplate();
      ordered.componentPropertyBindings = new[]
      {
        ordered.componentPropertyBindings[0],
        new ComponentPropertyBinding
        {
          SourcePath = nameof(Item.OtherPosition), TargetComponent = ordered.transform, TargetPath = "localPosition",
        },
      };
      var reversed = CreateTemplate();
      reversed.componentPropertyBindings = new[]
      {
        new ComponentPropertyBinding
        {
          SourcePath = nameof(Item.OtherPosition), TargetComponent = reversed.transform, TargetPath = "localPosition",
        },
        reversed.componentPropertyBindings[0],
      };
      ordered.DataSource = new Item { Position = Vector3.right, OtherPosition = Vector3.up };
      reversed.DataSource = new Item { Position = Vector3.right, OtherPosition = Vector3.up };
      Assert.AreNotSame(Plan(ordered), Plan(reversed));
      Assert.AreEqual(Vector3.up, ordered.transform.localPosition);
      Assert.AreEqual(Vector3.right, reversed.transform.localPosition);
    }

    [Test]
    public void DifferentTargetTypesDoNotSharePlans()
    {
      var first = CreateTemplate();
      var rectObject = new GameObject("Rect template", typeof(RectTransform));
      objects.Add(rectObject);
      var second = rectObject.AddComponent<View>();
      second.componentPropertyBindings = new[]
      {
        new ComponentPropertyBinding
        {
          SourcePath = nameof(Item.Position), TargetComponent = second.transform, TargetPath = "localPosition",
        },
      };
      first.DataSource = new Item { Position = Vector3.right };
      second.DataSource = new Item { Position = Vector3.up };

      Assert.AreNotSame(Plan(first), Plan(second));
      Assert.AreEqual(Vector3.right, first.transform.localPosition);
      Assert.AreEqual(Vector3.up, second.transform.localPosition);
    }

    [Test]
    public void DifferentContainerSourcePathsDoNotSharePlans()
    {
      var template = CreateTemplate();
      var first = CreateContainer(template, new Item());
      var second = CreateContainer(template, new Item());
      second.containerPropertyBindings[0].SourcePath = nameof(Collection.OtherItems);
      var source = new Collection
      {
        Items = new[] { new Item { Position = Vector3.right } },
        OtherItems = new[] { new Item { Position = Vector3.up } },
      };
      first.DataSource = source;
      second.DataSource = source;

      Assert.AreNotSame(Plan(first), Plan(second));
      Assert.AreSame(Plan(Child(first)), Plan(Child(second)));
      Assert.AreEqual(Vector3.right, Child(first).transform.localPosition);
      Assert.AreEqual(Vector3.up, Child(second).transform.localPosition);
    }

    [Test]
    public void ResettingTheGlobalCacheRefreshesExistingViews()
    {
      var first = CreateTemplate();
      first.DataSource = new Item();
      var oldPlan = Plan(first);
      ClearGlobalPlans();
      first.DataSource = new Item { Position = Vector3.right };
      var second = CreateTemplate();
      second.DataSource = new Item { Position = Vector3.up };

      Assert.AreNotSame(oldPlan, Plan(first));
      Assert.AreSame(Plan(first), Plan(second));
    }

    [Test]
    public void NullElementsClearNestedContainersAndCanBeReplacedAgain()
    {
      var template = CreateTemplate();
      var leafTemplate = CreateTemplate();
      template.containerPropertyBindings = new[]
      {
        new ContainerPropertyBinding
        {
          SourcePath = nameof(Item.Items), TargetContainer = template.transform, ElementTemplate = leafTemplate,
        },
      };
      var container = CreateContainer(template, new Item { Items = new[] { new Item() } });
      var child = Child(container);
      Assert.AreEqual(1, child.transform.childCount);

      ((Collection)container.DataSource).NotifyChanges(source => source.Items = new INotifyDataSourceChanged[] { null });
      Assert.AreEqual(0, child.transform.childCount);
      ((Collection)container.DataSource).NotifyChanges(source => source.Items = new[]
      {
        new Item { Position = Vector3.up, Items = new[] { new Item() } },
      });
      Assert.AreEqual(1, child.transform.childCount);
      Assert.AreEqual(Vector3.up, child.transform.localPosition);
    }

    private View CreateTemplate()
    {
      var view = CreateView("Template");
      view.componentPropertyBindings = new[]
      {
        new ComponentPropertyBinding
        {
          SourcePath = nameof(Item.Position), TargetComponent = view.transform, TargetPath = "localPosition",
        },
      };
      return view;
    }

    private View CreateNestedTemplate(int depth)
    {
      var template = CreateTemplate();
      if (depth == 0) return template;
      var leafTemplate = CreateNestedTemplate(depth - 1);
      leafTemplate.transform.SetParent(template.transform);
      var target = new GameObject("Nested target");
      target.transform.SetParent(template.transform);
      template.containerPropertyBindings = new[]
      {
        new ContainerPropertyBinding
        {
          SourcePath = nameof(Item.Items), TargetContainer = target.transform, ElementTemplate = leafTemplate,
        },
      };
      return template;
    }

    private static Item CreateNestedItem(int depth, Vector3 position) => depth == 0
      ? new Item { Position = position }
      : new Item { Items = new[] { CreateNestedItem(depth - 1, position) } };

    private View CreateContainer(View template, params INotifyDataSourceChanged[] items)
    {
      var view = CreateView("Container");
      view.containerPropertyBindings = new[]
      {
        new ContainerPropertyBinding
        {
          SourcePath = nameof(Collection.Items), TargetContainer = view.transform, ElementTemplate = template,
        },
      };
      view.DataSource = new Collection { Items = items };
      return view;
    }

    private View CreateView(string name)
    {
      var gameObject = new GameObject(name);
      objects.Add(gameObject);
      return gameObject.AddComponent<View>();
    }

    private static View Child(View container, int index = 0) => container.transform.GetChild(index).GetComponent<View>();

    private static object Plan(View view) => typeof(View)
      .GetField("bindingPlan", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);

    private static ConcurrentDictionary<(Type, string, PathResolveOperation), MethodInfo[]> PathCache() =>
      (ConcurrentDictionary<(Type, string, PathResolveOperation), MethodInfo[]>)typeof(CachedReflectionOperations)
        .GetField("propertyPathCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(CachedReflectionOperations.Instance);

    private static void ClearGlobalPlans() => typeof(View).Assembly.GetType("JH.DataBinding.ViewBindingPlans")
      .GetMethod("ClearCache", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
  }
}
