using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JH.DataBinding
{
  [Serializable]
  public class ContainerPropertyBinding
  {
    [SerializeField]
    [EnumerableBindingSourcePath]
    private string sourcePath;

    [SerializeField]
    private Transform targetContainer;

    [SerializeField]
    private View elementTemplate;

    private object dataSource;

    private MethodInfo[] dataSourcePropertyAccessors;
    private Action configurationChanged;
    private ViewBindingPlans elementBindingPlans;
    private ViewBindingPlan elementBindingPlan;
    private Type elementSourceType;

    internal void ObserveConfiguration(Action onConfigurationChanged) =>
      configurationChanged = onConfigurationChanged;

    internal void SetPreparedDataSource(object source) => dataSource = source;

    internal void BindPrepared(object source, MethodInfo[] sourceAccessors)
    {
      dataSource = source;
      dataSourcePropertyAccessors = sourceAccessors;
    }

    public object DataSource
    {
      get => dataSource;
      set
      {
        dataSource = value;
        BindSource();
      }
    }

    public string SourcePath
    {
      get => sourcePath;
      set
      {
        if (sourcePath == value)
        {
          return;
        }

        sourcePath = value;
        configurationChanged?.Invoke();
        BindSource();
      }
    }

    public Transform TargetContainer
    {
      get => targetContainer;
      set => targetContainer = value;
    }

    public View ElementTemplate
    {
      get => elementTemplate;
      set
      {
        if (elementTemplate != value)
        {
          elementTemplate = value;
          configurationChanged?.Invoke();
        }
      }
    }

    private void BindSource()
    {
      if (dataSource != null && !String.IsNullOrEmpty(sourcePath))
      {
        dataSourcePropertyAccessors = RuntimeReflectionOperations.Instance
          .ResolvePublicPropertyPath(dataSource, SourcePath, PathResolveOperation.GetValue);
      }
      else
      {
        dataSourcePropertyAccessors = Array.Empty<MethodInfo>();
      }
    }

    public void UpdateBinding()
    {
      if (
        (dataSourcePropertyAccessors != null)
        && (ElementTemplate != null)
        && (TargetContainer != null)
      )
      {
        if (dataSource is null)
        {
          RemoveSuperfluousChildren(0);
          return;
        }

        if (dataSourcePropertyAccessors.Length == 0)
        {
          return;
        }

        var boundInstances = (
          (
            RuntimeReflectionOperations.Instance.InvokeGetAccessChain(dataSourcePropertyAccessors, dataSource)
            as IEnumerable<INotifyDataSourceChanged>
          ) ?? Array.Empty<INotifyDataSourceChanged>()
        ).ToArray();

        if (boundInstances.Length == 0)
        {
          RemoveSuperfluousChildren(0);
          return;
        }

        var plans = ElementTemplate.PrepareBindingPlans();
        if (plans != elementBindingPlans)
        {
          elementBindingPlans = plans;
          elementBindingPlan = null;
        }

        AddMissingChildren(boundInstances.Length, plans);
        RemoveSuperfluousChildren(boundInstances.Length);

        for (var childIndex = 0; childIndex < boundInstances.Length; childIndex++)
        {
          // ReSharper disable once Unity.PerformanceCriticalCodeInvocation
          var view = TargetContainer.GetChild(childIndex).GetComponent<View>();
          var source = boundInstances[childIndex];
          var sourceType = source?.GetType();
          if (elementBindingPlan == null || sourceType != elementSourceType)
          {
            elementBindingPlan = plans.ForSource(source);
            elementSourceType = sourceType;
          }

          view.SetDataSource(source, elementBindingPlan);
        }
      }
    }

    private void AddMissingChildren(int numberOfRequiredChildren, ViewBindingPlans plans)
    {
      while (TargetContainer.childCount < numberOfRequiredChildren)
      {
        var view = UnityEngine.Object.Instantiate(ElementTemplate, TargetContainer);
        view.AttachTemplateBindingPlans(ElementTemplate, plans);
      }
    }

    private void RemoveSuperfluousChildren(int numberOfChildrenToKeep)
    {
      while (TargetContainer.childCount > numberOfChildrenToKeep)
      {
        var child = TargetContainer.GetChild(TargetContainer.childCount - 1);
        var childView = child.GetComponent<View>();

        if (childView != null)
        {
          childView.DataSource = null;
        }

        child.SetParent(null);
        UnityEngine.Object.Destroy(child.gameObject);
      }
    }
  }
}
