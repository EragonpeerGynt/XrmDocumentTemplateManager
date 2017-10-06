﻿using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Futurez.Entities
{
    [DefaultProperty("Name")]
    public partial class DocumentTemplateEdit
    {
        public DocumentTemplateEdit(Entity template)
        {
            this.Id = template.Id;
            this.Name = template.GetAttribValue<string>("name");
            this.Description = template.GetAttribValue<string>("description");
            this.Type = template.GetFormattedAttribValue("documenttype");
            this.TypeValue = template.GetAttribValue<OptionSetValue>("documenttype").Value;
            this.AssocaitedEntity = template.GetFormattedAttribValue("associatedentitytypecode");
            this.Status = template.GetFormattedAttribValue("status");

            var entityRef = template.GetAttribValue<EntityReference>("createdby");
            this.CreatedBy = (entityRef != null) ? entityRef.Name : null;

            var dt = template.GetAttribValue<DateTime?>("createdon");
            if (dt.HasValue) {
                this.CreatedOn = dt.Value.ToLocalTime();
            }

            entityRef = template.GetAttribValue<EntityReference>("modifiedby");
            this.ModifiedBy = (entityRef != null) ? entityRef.Name : null;

            dt = template.GetAttribValue<DateTime?>("modifiedon");
            if (dt.HasValue) {
                this.ModifiedOn = dt.Value.ToLocalTime();
            }

            this.EntityLogicalName = template.LogicalName;
            this.TemplateScope = (template.LogicalName == "documenttemplate") ? "System" : "Personal";

            var content = template.GetAttribValue<string>("content");
            this.Content = (content == null) ? null : Convert.FromBase64String(content);
        }
        #region Attributes
        [Description("Entity Logical Name")]
        [Category("General")]
        [Browsable(false)]
        [DisplayName("Entity Logical Name")]
        public string EntityLogicalName { get; private set; }

        [Description("Document Template Id")]
        [Category("General")]
        [Browsable(false)]
        public Guid Id { get; private set; }

        [Description("Document Template Name")]
        [Category("General")]
        public string Name { get; private set; }

        [Description("Description for this Document Template")]
        [Category("General")]
        [EditorAttribute("System.ComponentModel.Design.MultilineStringEditor, System.Design", "System.Drawing.Design.UITypeEditor")]
        public string Description { get; private set; }

        [Description("Current Document Template Status")]
        [Category("Locked")]
        public string Status { get; private set; }

        [DisplayName("Associated Entity Type Code")]
        [Description("Entity to which this Document Template is assocaited")]
        [Category("Locked")]
        public string AssocaitedEntity { get; private set; }

        [DisplayName("Created On")]
        [Description("Date/Time on which this Document Template was created")]
        [Category("Locked")]
        public DateTime? CreatedOn { get; private set; }

        [DisplayName("Created By")]
        [Description("System user who created this Document Template")]
        [Category("Locked")]
        public string CreatedBy { get; private set; }

        [DisplayName("Modified On")]
        [Description("Date/Time on which this Document Template was last modified")]
        [Category("Locked")]
        public DateTime? ModifiedOn { get; private set; }

        [DisplayName("Modified By")]
        [Description("System user who last modified this Document Template")]
        [Category("Locked")]
        public string ModifiedBy { get; private set; }

        [Description("Document Template Type")]
        [DisplayName("Content Type")]
        [Category("Locked")]
        public string Type { get; private set; }

        [Description("Document Template Type")]
        [DisplayName("Content Type")]
        [Category("Locked")]
        [Browsable(false)]
        public int TypeValue { get; private set; }

        [Description("Language setting for this Document Template")]
        [Category("Locked")]
        [Browsable(false)]
        public string Language { get; private set; }

        [Description("System or Personal document template")]
        [Category("General")]
        [DisplayName("Template Scope")]
        public string TemplateScope { get; private set; }

        [Description("Document Content")]
        [Category("General")]
        [Browsable(false)]
        [DisplayName("Document Content")]
        public byte[] Content { get; private set; }

        #endregion

        #region Helper Methods 
        /// <summary>
        ///  Retrieve all System document templates 
        /// </summary>
        /// <returns></returns>
        public static List<DocumentTemplateEdit> GetAllSystemTemplates(IOrganizationService service)
        {
            var templates = new List<DocumentTemplateEdit>();

            var fetchXml = "<fetch>" +
                  "<entity name='documenttemplate'> " +
                    "<attribute name='status' /> " +
                    "<attribute name='associatedentitytypecode' /> " +
                    "<attribute name='name' /> " +
                    "<attribute name='documenttypename' /> " +
                    "<attribute name='associatedentitytypecodename' /> " +
                    "<attribute name='organizationidname' /> " +
                    "<attribute name='statusname' /> " +
                    "<attribute name='documenttype' /> " +
                    "<attribute name='modifiedby' /> " +
                    "<attribute name='modifiedbyname' /> " +
                    "<attribute name='modifiedon' /> " +
                    "<attribute name='createdby' /> " +
                    "<attribute name='createdbyname' /> " +
                    "<attribute name='createdon' /> " +
                    "<attribute name='organizationid' /> " +
                    "<attribute name='documenttemplateid' /> " +
                    "<attribute name = 'description' /> " +
                  "</entity> " +
                "</fetch> ";

            var results = service.RetrieveMultiple(new FetchExpression(fetchXml));

            foreach (var entity in results.Entities)
            {
                templates.Add(new DocumentTemplateEdit(entity));
            }
            return templates;
        }

        /// <summary>
        /// Helper method to retrieve system document templates by Id 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="templateIds"></param>
        /// <param name="includeContent"></param>
        /// <returns></returns>
        public static EntityCollection GetDocumentTemplatesById(IOrganizationService service, List<Guid> templateIds, bool includeContent)
        {
            var query = new QueryExpression() {
                EntityName = "documenttemplate",
                ColumnSet = new ColumnSet("name", "documenttype", "documenttemplateid", "status"),
                Criteria = new FilterExpression(LogicalOperator.And) {
                    Conditions = { new ConditionExpression("documenttemplateid", ConditionOperator.In, templateIds) }
                }
            };

            if (includeContent) {
                query.ColumnSet.AddColumn("content");
            }

            var templates = service.RetrieveMultiple(query);

            return templates;
        }
        #endregion
    }
}