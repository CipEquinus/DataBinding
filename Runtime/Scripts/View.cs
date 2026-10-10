using System;
using System.Diagnostics;
using UnityEngine;

namespace JH.DataBinding
{
  [DebuggerDisplay("View({name}) : {DataSource}")]
  public class View : MonoBehaviour
  {
#if UNITY_EDITOR
    [DataSourceType]
    public SerializableType dataSourceType;
#endif // UNITY_EDITOR

    public ComponentPropertyBinding[] componentPropertyBindings =
      Array.Empty<ComponentPropertyBinding>();

    public ContainerPropertyBinding[] containerPropertyBindings =
      Array.Empty<ContainerPropertyBinding>();

    private INotifyDataSourceChanged dataSource;
    private ViewBindingPlans templateBindingPlans;
    private ViewBindingPlan bindingPlan;
    private Action bindingConfigurationChanged;
    private ComponentPropertyBinding[] plannedComponentBindings;
    private ContainerPropertyBinding[] plannedContainerBindings;
    private int bindingPlanGeneration;

    internal ViewBindingPlans PrepareBindingPlans()
    {
      if (templateBindingPlans == null || bindingPlanGeneration != ViewBindingPlans.Generation
        || plannedComponentBindings != componentPropertyBindings
        || plannedContainerBindings != containerPropertyBindings)
      {
        bindingPlan = null;
        templateBindingPlans = ViewBindingPlans.ForTemplate(this);
        ObserveBindingConfiguration();
      }

      return templateBindingPlans;
    }

    /// <summary>
    /// Invalidates this View's prepared layout after editing entries in an existing
    /// binding array. Property setters and replacing arrays invalidate automatically.
    /// </summary>
    public void InvalidateBindingPlans()
    {
      bindingPlan = null;
      templateBindingPlans = null;
    }

    private void ObserveBindingConfiguration()
    {
      plannedComponentBindings = componentPropertyBindings;
      plannedContainerBindings = containerPropertyBindings;
      bindingPlanGeneration = ViewBindingPlans.Generation;
      bindingConfigurationChanged ??= InvalidateBindingPlans;
      foreach (var binding in componentPropertyBindings)
      {
        binding.ObserveConfiguration(bindingConfigurationChanged);
      }

      foreach (var binding in containerPropertyBindings)
      {
        binding.ObserveConfiguration(bindingConfigurationChanged);
      }
    }

    // Instantiate already preserves the layout. Propagate its prepared definition
    // to cloned nested templates too, instead of reconstructing their layouts.
    internal void AttachTemplateBindingPlans(View template, ViewBindingPlans plans)
    {
      if (templateBindingPlans == plans && bindingPlanGeneration == ViewBindingPlans.Generation
        && plannedComponentBindings == componentPropertyBindings
        && plannedContainerBindings == containerPropertyBindings)
      {
        return;
      }

      templateBindingPlans = plans;
      bindingPlan = null;
      ObserveBindingConfiguration();
      for (var i = 0; i < containerPropertyBindings.Length; ++i)
      {
        var localTemplate = containerPropertyBindings[i].ElementTemplate;
        var originalTemplate = template.containerPropertyBindings[i].ElementTemplate;
        if (localTemplate != null && originalTemplate != null && localTemplate != originalTemplate)
        {
          localTemplate.AttachTemplateBindingPlans(originalTemplate, originalTemplate.PrepareBindingPlans());
        }
      }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
      // Inspector edits bypass the property setters. Invalidate on edit rather
      // than scanning every binding on each container update.
      InvalidateBindingPlans();
    }
#endif

    public virtual void OnEnable()
    {
      if (dataSource != null)
      {
        bindingPlan = PrepareBindingPlans().ForSource(dataSource);
      }

      BindBindingDataSources(dataSource);
      UpdateBindings();
    }

    public virtual void OnDestroy()
    {
      if (DataSource != null)
      {
        DataSource = null;
      }
    }

    public INotifyDataSourceChanged DataSource
    {
      get
      {
        // return local data source if available

        if (dataSource != null)
        {
          return dataSource;
        }

        // walk up the hierarchy if no local data source is available

        var parent = transform.parent;

        if (parent != null)
        {
          var parentViewBehaviour = parent.GetComponent<View>();

          if (parentViewBehaviour != null)
          {
            return parentViewBehaviour.DataSource;
          }
        }

        return null;
      }
      set => SetDataSource(value, null);
    }

    internal void SetDataSource(INotifyDataSourceChanged value, ViewBindingPlan plan)
    {
      var plans = PrepareBindingPlans();
      if (plan == null || plan.Definitions != plans)
      {
        plan = bindingPlan != null && bindingPlan.Definitions == plans
          && bindingPlan.SourceType == value?.GetType()
          ? bindingPlan
          : plans.ForSource(value);
      }

      var planChanged = bindingPlan != plan;
      var sourceChanged = dataSource != value;
      if (!sourceChanged && !planChanged)
      {
        return;
      }

      var oldDataSource = DataSource as INotifyDataSourceChanged;

      if (sourceChanged && oldDataSource != null)
      {
        oldDataSource.DataSourceChanged -= OnDataSourceChanged;
      }

      dataSource = value;
      bindingPlan = plan;
      BindBindingDataSources(dataSource, planChanged);

      var newDataSource = DataSource as INotifyDataSourceChanged;

      if (sourceChanged && newDataSource != null)
      {
        newDataSource.DataSourceChanged += OnDataSourceChanged;
      }

      UpdateBindings();
    }

    private void OnDataSourceChanged()
    {
      if (bindingPlan == null || bindingPlanGeneration != ViewBindingPlans.Generation
        || plannedComponentBindings != componentPropertyBindings
        || plannedContainerBindings != containerPropertyBindings)
      {
        bindingPlan = PrepareBindingPlans().ForSource(dataSource);
        BindBindingDataSources(dataSource);
      }

      UpdateBindings();
    }

    private void BindBindingDataSources(object bindingDataSource, bool installPlan = true)
    {
      if (bindingPlan != null)
      {
        if (installPlan)
        {
          bindingPlan.Bind(this, bindingDataSource);
        }
        else
        {
          foreach (var binding in componentPropertyBindings)
          {
            binding.SetPreparedDataSource(bindingDataSource);
          }

          foreach (var binding in containerPropertyBindings)
          {
            binding.SetPreparedDataSource(bindingDataSource);
          }
        }

        return;
      }

      foreach (var binding in componentPropertyBindings)
      {
        binding.DataSource = bindingDataSource;
      }

      foreach (var binding in containerPropertyBindings)
      {
        binding.DataSource = bindingDataSource;
      }
    }

    private void UpdateBindings()
    {
      foreach (var binding in componentPropertyBindings)
      {
        binding.UpdateBinding();
      }

      foreach (var binding in containerPropertyBindings)
      {
        binding.UpdateBinding();
      }
    }
  }
}
