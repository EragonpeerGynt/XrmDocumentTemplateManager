﻿using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using XrmToolBox.Extensibility.Args;

using Futurez.Entities;
using System.Text;

namespace Futurez.Xrm.Tools
{
    enum BulkAction
    {
        Activate = 1,
        Deactivate,
        Delete
    };
    public partial class DocTemplateManagerControl : PluginControlBase, IStatusBarMessenger // PluginControlBase UserControl
    {
        EditTemplateControl _editControl = null;
        UploadMultipleSummary _uploadSummary = null;

        public DocTemplateManagerControl()
        {
            InitializeComponent();
        }

        public event EventHandler<StatusBarMessageEventArgs> SendMessageToStatusBar;


        private void LoadDocumentTemplates() {
            LoadDocumentTemplates(null);
        }
        /// <summary>
        /// Load all of the system document templates from the current connection
        /// </summary>
        private void LoadDocumentTemplates(Action onCompleteCallback)
        {
            this.listViewDocumentTemplates.Items.Clear();
            this.listViewDocumentTemplates.Refresh();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Retrieving the list of Document Templates",
                Work = (w, e) =>
                {
                    var listViewItemsColl = new List<ListViewItem>();
                    try
                    {
                        this.listViewDocumentTemplates.SuspendLayout();

                        w.ReportProgress(0, "Loading Templates from CRM");
                        var templates = DocumentTemplateEdit.GetAllSystemTemplates(this.Service);

                        int progress = 0;
                        int counter = 0;
                        foreach (var template in templates)
                        {
                            progress = counter / templates.Count * 100;
                            w.ReportProgress(progress, "New template: " + template.Name);

                            // we don't want the content!
                            var docType = template.Type;
                            var lvItem = new ListViewItem()
                            {
                                Name = "Name",
                                Text = template.Name,
                                Tag = template,  // stash the template here so we can view details later
                                Group = this.listViewDocumentTemplates.Groups[docType]
                            };

                            lvItem.SubItems.Add(new ListViewItem.ListViewSubItem(lvItem, template.Status) { Tag = "Status", Name = "Status" });
                            lvItem.SubItems.Add(new ListViewItem.ListViewSubItem(lvItem, template.CreatedOn.ToString()) { Tag = "CreatedOn", Name = "CreatedOn" });
                            lvItem.SubItems.Add(new ListViewItem.ListViewSubItem(lvItem, template.ModifiedOn.ToString()) { Tag = "ModifiedOn", Name = "ModifiedOn" });
                            lvItem.SubItems.Add(new ListViewItem.ListViewSubItem(lvItem, template.AssocaitedEntity) { Tag = "Associated Entity", Name = "Associated Entity" });

                            listViewItemsColl.Add(lvItem);
                        }
                        
                        w.ReportProgress(100, "Loading Templates Complete");
                        w.ReportProgress(0, "");

                        // NOW add the items to avoid flicker
                        listViewDocumentTemplates.Items.AddRange(listViewItemsColl.ToArray<ListViewItem>());
                        listViewDocumentTemplates.ResumeLayout();

                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                },
                ProgressChanged = e =>
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(e.ProgressPercentage, e.UserState.ToString()));
                },
                PostWorkCallBack = e =>
                {
                    // now update the UI with the new selection 
                    UpdateControlsForSelection();

                    // invoke the callback if passed
                    onCompleteCallback?.Invoke();
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }

        #region Document Template related methods 
        /// <summary>
        /// Download the document templates selected in the list
        /// </summary>
        private void PerformDownloadTemplates()
        {
            // choose a folder for the save...
            var folderDlg = new FolderBrowserDialog()
            {
                Description = "Select a destination folder for your download",
                RootFolder = Environment.SpecialFolder.MyComputer,
                ShowNewFolderButton = true
            };

            if ((folderDlg.ShowDialog() == DialogResult.OK) && (folderDlg.SelectedPath != null))
            {
                ExecuteMethod(DownloadDocumentTemplates, folderDlg.SelectedPath);
            }
        }

        /// <summary>
        /// Allow the user to edit some of the template attriutes
        /// Show the edit control, allow user to update attributes on a single item
        /// </summary>
        private void PerformEditTemplate()
        {
            ListViewItem item = this.listViewDocumentTemplates.SelectedItems[0];
            var template = item.Tag as DocumentTemplateEdit;

            // new instance of the edit control
            _editControl = new EditTemplateControl(this.Service, template)
            {
                // center me
                Top = Convert.ToInt32(this.Parent.Height / 2) - 110,
                Left = Convert.ToInt32(this.Parent.Width / 2) - 160,
                Parent = this
            };

            // disable the main UI so they don't monkey with stuff
            ToggleMainControlsEnabled(false);

            // handle the on complete event so we can reload and clean up control
            _editControl.OnComplete += EditControl_OnComplete;
            _editControl.BringToFront();
            _editControl.Show();
        }

        /// <summary>
        /// Delete the templates selected in the list 
        /// </summary>
        private void PerformDeleteTemplates()
        {
            var confirmMessage = "Would you like to delete the selected templates from CRM?\nNOTE: THIS ACTION CANNOT BE UNDONE.";
            if (MessageBox.Show(confirmMessage, "Confrim Delete Template(s)", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                // get the file up there!
                ExecuteMethod(PerformBulkActionTemplates, BulkAction.Delete);
            }
        }

        private void PerformActivateTemplates()
        {
            var confirmMessage = "Would you like to activate the selected templates in CRM?";
            if (MessageBox.Show(confirmMessage, "Confrim Activate Template(s)", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                // get the file up there!
                ExecuteMethod(PerformBulkActionTemplates, BulkAction.Activate);
            }
        }

        private void PerformDeactivateTemplates()
        {
            var confirmMessage = "Would you like to deactivate the selected templates in CRM?";
            if (MessageBox.Show(confirmMessage, "Confrim Deactivate Template(s)", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                // get the file up there!
                ExecuteMethod(PerformBulkActionTemplates, BulkAction.Deactivate);
            }
        }
        
        /// <summary>
        /// Shared method that will perform bulk actions for Delete, Activate/Deactivate
        /// </summary>
        /// <param name="action"></param>
        private void PerformBulkActionTemplates(BulkAction action)
        {
            var message = "";
            var progressMessage = "";
            switch (action)
            {
                case BulkAction.Activate:
                    message = "Activating selected Document Templates";
                    progressMessage = "activating Document Templates in CRM";
                    break;
                case BulkAction.Deactivate:
                    message = "Deactivating selected Document Templates";
                    progressMessage = "deactivating Document Templates in CRM";
                    break;
                case BulkAction.Delete:
                    message = "Deleting selected Document Templates";
                    progressMessage = "deleting Document Templates from CRM";
                    break;
            }

            // build the bach list for delete 
            WorkAsync(new WorkAsyncInfo
            {
                Message = message,
                Work = (w, e) =>
                {
                    try
                    {
                        w.ReportProgress(0, "Begin " + progressMessage);

                        int templateCount = this.listViewDocumentTemplates.SelectedItems.Count;
                        int counter = 0;

                        // grab the list of selected template Ids
                        foreach (ListViewItem item in this.listViewDocumentTemplates.SelectedItems)
                        {
                            counter++;
                            var template = item.Tag as DocumentTemplateEdit;
                            var entityTarget = new Entity(template.EntityLogicalName, template.Id) {
                                EntityState = EntityState.Changed
                            };

                            w.ReportProgress(Convert.ToInt32(counter / templateCount) * 100, "Template: " + template.Name);

                            switch (action)
                            {
                                case BulkAction.Activate:
                                case BulkAction.Deactivate:
                                    if (action == BulkAction.Activate) {
                                        entityTarget["status"] = false;
                                    }
                                    else {
                                        entityTarget["status"] = true;
                                    }
                                    var updateRequest = new UpdateRequest() {
                                        Target = entityTarget
                                    };
                                    this.Service.Execute(updateRequest);
                                    break;
                                case BulkAction.Delete:
                                    var deleteRequest = new DeleteRequest() {
                                        Target = entityTarget.ToEntityReference()
                                    };
                                    this.Service.Execute(deleteRequest);
                                    break;
                            }
                        }

                        w.ReportProgress(0, "Done " + progressMessage);
                        w.ReportProgress(0, "");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("An error occured with one or more templates:\n" + ex.Message);
                    }
                },
                ProgressChanged = e =>
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(e.ProgressPercentage, e.UserState.ToString()));
                },
                PostWorkCallBack = e => {
                    // reload the list of templates 
                    LoadDocumentTemplates();
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }
        #endregion

        #region Common UI methods 
        /// <summary>
        /// Toggle the main area enabled or disabled
        /// </summary>
        /// <param name="enabled"></param>
        private void ToggleMainControlsEnabled(bool enabled)
        {
            this.tableLayoutPanelMain.Enabled = enabled;
            this.toolStripMain.Enabled = enabled;
            this.panelSplitter.Enabled = enabled;
            this.labelInstructions.Enabled = enabled;
        }

        /// <summary>
        /// Based on the list selection, update the controls, toolbar and properties
        /// </summary>
        private void UpdateControlsForSelection()
        {
            var count = 0;

            if (this.listViewDocumentTemplates.SelectedItems != null) {
                count = this.listViewDocumentTemplates.SelectedItems.Count;
            }

            // now that items have been loaded, enable upload multiple
            this.toolStripMenuItemUploadMultiple.Enabled = true;

            bool singleSelect = (count == 1);
            bool multiSelect = (count > 0);

            // toolButtonUpload.Enabled = singleSelect;
            toolStripMenuItemEditTemplate.Enabled = singleSelect;
            toolStripMenuItemUploadSingle.Enabled = singleSelect;

            toolButtonDownload.Enabled = multiSelect;
            toolStripMenuItemDeleteTemplates.Enabled = multiSelect;
            toolStripMenuItemDeactivateTemplates.Enabled = multiSelect;
            toolStripMenuItemActivateTemplates.Enabled = multiSelect;

            this.propertyGridDetails.SelectedObject = null;

            if (count == 1)
            {
                var item = this.listViewDocumentTemplates.SelectedItems[0];
                var template = item.Tag as DocumentTemplateEdit;
                this.propertyGridDetails.SelectedObject = template;
            }
        }
        #endregion

        #region ListView Event Handlers
        private void listViewDocumentTemplates_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateControlsForSelection();
        }

        private void listViewDocumentTemplates_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            listViewDocumentTemplates.SuspendLayout();

            if (e.Column == int.Parse(listViewDocumentTemplates.Tag.ToString()))
            {
                listViewDocumentTemplates.Sorting = ((listViewDocumentTemplates.Sorting == SortOrder.Ascending) ? SortOrder.Descending : SortOrder.Ascending);
                listViewDocumentTemplates.ListViewItemSorter = new ListViewItemComparer(e.Column, listViewDocumentTemplates.Sorting);
                return;
            }
            listViewDocumentTemplates.Tag = e.Column;
            listViewDocumentTemplates.ListViewItemSorter = new ListViewItemComparer(e.Column, SortOrder.Ascending);

            listViewDocumentTemplates.ResumeLayout();
        }

        private void listViewDocumentTemplates_DoubleClick(object sender, EventArgs e)
        {
            UpdateControlsForSelection();
            PerformEditTemplate();
        }
        
        private void listViewDocumentTemplates_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.A))
            {
                this.listViewDocumentTemplates.SuspendLayout();
                foreach (ListViewItem item in this.listViewDocumentTemplates.Items)
                {
                    item.Selected = true;
                }
                this.listViewDocumentTemplates.ResumeLayout();
            }
        }
        #endregion 

        #region Toolbar Button Event Handlers
        private void toolButtonCloseTab_Click(object sender, EventArgs e)
        {
            this.CloseTool();
        }

        private void toolButtonLoadTemplates_Click(object sender, EventArgs e)
        {
            // make sure they want to reload
            if (this.listViewDocumentTemplates.Items.Count > 0)
            {
                if (MessageBox.Show("Clear the current list of Document Templates and reload from the server?", "Load Templates", MessageBoxButtons.YesNo) != DialogResult.Yes) {
                    return;
                }
            }
            // otherwise load
            ExecuteMethod(LoadDocumentTemplates);
        }

        private void toolButtonDownload_Click(object sender, EventArgs e)
        {
            if (this.listViewDocumentTemplates.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more templates to download.");
            }
            else
            {
                PerformDownloadTemplates();
            }
        }

        private void toolButtonUpload_Click(object sender, EventArgs e)
        {
        }

        private void toolStripMenuItemDeleteTemplates_Click(object sender, EventArgs e)
        {
            if (this.listViewDocumentTemplates.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more templates to delete.");
            }
            else
            {
                PerformDeleteTemplates();
            }
        }

        private void toolStripMenuItemDeactivateTemplates_Click(object sender, EventArgs e)
        {
            if (this.listViewDocumentTemplates.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more templates to deactivate.");
            }
            else
            {
                PerformDeactivateTemplates();
            }
        }
        private void toolStripMenuItemActivateTemplates_Click(object sender, EventArgs e)
        {
            if (this.listViewDocumentTemplates.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more templates to activate.");
            }
            else
            {
                PerformActivateTemplates();
            }
        }

        private void toolStripMenuItemEditTemplate_Click(object sender, EventArgs e)
        {
            if (this.listViewDocumentTemplates.SelectedItems.Count != 1)
            {
                MessageBox.Show("Please select only one template to edit.");
            }
            else
            {
                PerformEditTemplate();
            }
        }
        #endregion

        #region File Access methods
        /// <summary>
        /// Download the selected templates
        /// </summary>
        /// <param name="saveFolder"></param>
        private void DownloadDocumentTemplates(string saveFolder)
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Retrieving the Document Template Content...",
                Work = (w, e) =>
                {
                    try
                    {
                        var renameCount = 1;
                        var templateIds = new List<Guid>();
                        // grab the list of selected template Ids
                        foreach (ListViewItem item in this.listViewDocumentTemplates.SelectedItems)
                        {
                            var template = item.Tag as DocumentTemplateEdit;
                            templateIds.Add(template.Id);
                        }

                        // retrieve the doc template contents
                        var templates = DocumentTemplateEdit.GetDocumentTemplatesById(this.Service, templateIds, true);

                        byte[] bytesToSave;
                        var downloadFileName = "";
                        Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
                        var fileCount = templates.Entities.Count;

                        foreach (Entity template in templates.Entities)
                        {
                            var docTemplate = new DocumentTemplateEdit(template);

                            var name = docTemplate.Name;
                            var ext =  docTemplate.TypeValue == 1
                                        ? Constants.FILE_EXT_EXCEL :
                                        Constants.FILE_EXT_WORD;

                            // make sure we have unique names
                            var fileName = name + ext;
                            var folder = docTemplate.Status + "\\" + docTemplate.Type + "\\";
                            
                            // if we have more than one file, add a folder for the status
                            if (fileCount > 1) {
                                fileName = folder + fileName;
                            }

                            renameCount = 1;
                            while (files.ContainsKey(fileName)) {
                                if (fileCount > 1) {
                                    fileName = folder + name + " (" + (renameCount++).ToString() + ")" + ext;
                                }
                                else {
                                    fileName = name + " (" + (renameCount++).ToString() + ")" + ext;
                                }
                            }

                            // add the file to the list for download.
                            files.Add(fileName, docTemplate.Content);
                        }

                        // if only one file was selected, save just that... otherwise, package things up in a zip!
                        if (files.Count > 1) {
                            var zipper = new ZipBuilder();
                            downloadFileName = string.Format("Document Templates - {0:M-d-yyyy HH-mm}.zip", DateTime.Now);
                            bytesToSave = zipper.zipBytes(files);
                        }
                        else {
                            var file = files.First();
                            bytesToSave = file.Value;
                            downloadFileName = file.Key;
                        }
                        // append the selected folder... 
                        downloadFileName = Path.Combine(saveFolder, downloadFileName);

                        renameCount = 1;
                        // delete file if it exists
                        while (File.Exists(downloadFileName)) {
                            downloadFileName = Path.GetFileNameWithoutExtension(downloadFileName) + " (" + (renameCount++).ToString() + ")" + Path.GetExtension(downloadFileName);
                            downloadFileName = Path.Combine(saveFolder, downloadFileName);
                        }
                        // write 
                        using (var saveMe = new FileStream(downloadFileName, FileMode.OpenOrCreate, FileAccess.Write))
                        {
                            saveMe.Write(bytesToSave, 0, bytesToSave.Length);
                            saveMe.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("The following error occurred attempting to download your templates.\n" + ex.Message);
                    }
                },
                ProgressChanged = e =>
                {
                    // If progress has to be notified to user, use the following method:
                    // SetWorkingMessage("Downloading your templates now...");
                    
                },
                PostWorkCallBack = e => { },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }

        private void PerformUploadSingle() {

            // assuming only a single selection
            ListViewItem item = this.listViewDocumentTemplates.SelectedItems[0];
            var template = item.Tag as DocumentTemplateEdit;

            // grab the list of files to be uploaded
            using (var openDlg = new OpenFileDialog() {
                InitialDirectory = Path.GetPathRoot(Environment.SystemDirectory),
                Filter = Constants.GetFileOpenFilter(),
                FilterIndex = 1,
                RestoreDirectory = true
            })
            {
                if (openDlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var fileUpload = new FileUpload(openDlg.FileName);
                        if (template.Type != fileUpload.TemplateType) {
                            MessageBox.Show("The extension of the file selected does not match the template selected.", 
                                "Incorrect File Type", MessageBoxButtons.OK);
                            return;
                        }
                        else {
                            var confirmMessage = "NOTE: This will overwrite the Content of the Document Template '" + template.Name + "'.  Please be sure that the Document Type and Associated Entity are correct.\n Would you like to continue?";
                            if (MessageBox.Show(confirmMessage, "Confrim Upload", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                // get the file up there!
                                ExecuteMethod(UploadFile, openDlg.FileName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error opening file : " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Upload multiple document template files
        /// </summary>
        private void PerformUploadMultiple()
        {
            // allow user to select one or more files 
            using (var openDlg = new OpenFileDialog() {
                InitialDirectory = Path.GetPathRoot(Environment.SystemDirectory),
                Filter = Constants.GetFileOpenFilter(),
                FilterIndex = 1,
                RestoreDirectory = true,
                Multiselect = true
            })
            {
                if (openDlg.ShowDialog() == DialogResult.OK)
                {
                    var templateNameList = new List<string>();
                    var templateList = new List<DocumentTemplateEdit>();

                    // grab the list of existing documents from the list view.
                    foreach (ListViewItem listItem in this.listViewDocumentTemplates.Items)
                    {
                        var template = (DocumentTemplateEdit)listItem.Tag;
                        templateNameList.Add(template.Name.ToLower() + "_" + template.Type);
                        templateList.Add(template);
                    }

                    var createTemplate = new List<FileUpload>();
                    var updateTemplate = new List<FileUpload>();
                    var ignoreTemplate = new List<FileUpload>();

                    // iterate on the list of files.  match each file name and extension to the template name and type
                    foreach (var file in openDlg.FileNames)
                    {
                        var fileUpload = new FileUpload(file);

                        // if the file extension is not .docx nor .xlsx, ignore 
                        if (fileUpload.IsIgnored)
                        {
                            ignoreTemplate.Add(fileUpload);
                        }
                        else {

                            var key = fileUpload.TemplateName.ToLower() + "_" + fileUpload.TemplateType;

                            var foundItems = templateNameList.FindAll(s => s == key);

                            // match on the file name and type
                            if (foundItems.Count == 0) {
                                // if the template is not found, then create
                                createTemplate.Add(fileUpload);
                            }
                            else {
                                // only one instance 
                                if (foundItems.Count == 1)
                                {
                                    var template = templateList.Find(t => t.Name.ToLower() == fileUpload.TemplateName.ToLower());
                                    fileUpload.TemplateId = template.Id;
                                    fileUpload.Note = "Update Template with id: " + template.Id.ToString("b");
                                    updateTemplate.Add(fileUpload);
                                }
                                else {
                                    fileUpload.IsIgnored = true;
                                    fileUpload.Note = "Duplicate template name found";
                                    ignoreTemplate.Add(fileUpload);
                                }
                            }
                        }
                    }

                    // present a summary, allow user to confirm/cancel
                    _uploadSummary = new UploadMultipleSummary(this.Service, createTemplate, updateTemplate, ignoreTemplate) {
                        Parent = this
                    };
                    ResizeSummaryScreen();
                    this.ParentForm.Resize += ParentForm_Resize;

                    // disable the main UI so they don't monkey with stuff
                    ToggleMainControlsEnabled(false);

                    // handle the on complete event so we can reload and clean up control
                    _uploadSummary.OnComplete += UploadSummary_OnComplete;
                    _uploadSummary.BringToFront();
                    _uploadSummary.Show();

                }
            }
        }

        private void ParentForm_Resize(object sender, EventArgs e)
        {
            ResizeSummaryScreen();
        }

        /// <summary>
        /// Helper method to resize the upload summary screen
        /// </summary>
        private void ResizeSummaryScreen() {

            var width = this.ParentForm.Width - 100;
            var height = this.ParentForm.Height - 250;

            if (_uploadSummary != null)
            {
                _uploadSummary.Top = Convert.ToInt32(this.ParentForm.Height / 2) - Convert.ToInt32(height / 2);
                _uploadSummary.Left = Convert.ToInt32(this.ParentForm.Width / 2) - Convert.ToInt32(width / 2);
                _uploadSummary.Height = height;
                _uploadSummary.Width = width;
            }
        }
        /// <summary>
        /// User elected to continue, upload the Create and Update list of files
        /// </summary>
        /// <param name="userCanceled"></param>
        /// <param name="createTemplate"></param>
        /// <param name="updateTemplate"></param>
        /// <param name="args"></param>
        private void UploadSummary_OnComplete(bool userCanceled, List<FileUpload> createTemplate, List<FileUpload> updateTemplate, EventArgs args)
        {
            _uploadSummary.Dispose();
            _uploadSummary = null;

            this.ParentForm.Resize -= ParentForm_Resize;

            // disable the main UI so they don't monkey with stuff
            ToggleMainControlsEnabled(true);

            if (!userCanceled)
            {
                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Uploading selected Documents",
                    Work = (w, e) =>
                    {
                        // read the file from disk
                        try
                        {
                            var errorMessages = new StringBuilder();
                            var counter = 1;
                            var templateCount = updateTemplate.Count + createTemplate.Count;

                            // make the content updates
                            // don't use bulk update to trap errors and not overload the request
                            foreach (var file in updateTemplate)
                            {
                                var contentBytes = File.ReadAllBytes(file.FullLocalPath);
                                var request = new UpdateRequest()
                                {
                                    Target = new Entity()
                                    {
                                        Id = file.TemplateId,
                                        LogicalName = "documenttemplate",
                                        EntityState = EntityState.Changed
                                    }
                                };
                                request.Target.Attributes["content"] = Convert.ToBase64String(contentBytes);

                                w.ReportProgress(Convert.ToInt32(counter++ / templateCount) * 100, "Updating template: " + file.TemplateName);
                                // gather up error messages and continue
                                try {
                                    this.Service.Execute(request);
                                }
                                catch (Exception ex) {
                                    errorMessages.AppendFormat("Error updating template: {0}, Error Message: {1}\n", file.FileName, ex.Message);
                                }
                            }

                            foreach (var file in createTemplate)
                            {
                                var contentBytes = File.ReadAllBytes(file.FullLocalPath);
                                var request = new CreateRequest() {
                                    Target = new Entity()
                                    {
                                        LogicalName = "documenttemplate",
                                        EntityState = EntityState.Created
                                    }
                                };
                                request.Target.Attributes["name"] = file.TemplateName;
                                request.Target.Attributes["documenttype"] = new OptionSetValue(file.TemplateTypeValue);
                                request.Target.Attributes["description"] = "New " + file.TemplateType + " template. Source file: " + file.FileName;
                                request.Target.Attributes["content"] = Convert.ToBase64String(contentBytes);

                                w.ReportProgress(Convert.ToInt32(counter++ / templateCount) * 100, "Creating template: " + file.TemplateName);

                                // gather up error messages and continue
                                try {
                                    this.Service.Execute(request);
                                }
                                catch (Exception ex) {
                                    errorMessages.AppendFormat("Error creating template: {0}, Error Message: {1}\n", file.FileName, ex.Message);
                                }
                            }

                            // if we have error messages, show them the error of their ways
                            if (errorMessages.Length > 0) {
                                MessageBox.Show("The following errors occured attempting to upload the files\n" + errorMessages.ToString(), "Errors During Upload", MessageBoxButtons.OK);
                            }
                        }
                        catch (Exception ex) {
                            MessageBox.Show("An error occured uploading the files\n" + ex.Message);
                        }
                    },
                    ProgressChanged = e =>
                    {
                        // If progress has to be notified to user, use the following method:
                        SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(e.ProgressPercentage, e.UserState.ToString()));
                    },
                    PostWorkCallBack = e =>
                    {
                        LoadDocumentTemplates();
                    },
                    AsyncArgument = null,
                    IsCancelable = true,
                    MessageWidth = 340,
                    MessageHeight = 150
                });
            }
        }
        /// <summary>
        /// Upload a single document template
        /// </summary>
        /// <param name="fileName"></param>
        private void UploadFile(string fileName)
        {
            var contentBytes = File.ReadAllBytes(fileName);

            ListViewItem item = this.listViewDocumentTemplates.SelectedItems[0];
            var template = item.Tag as DocumentTemplateEdit;

            var templateId = template.Id;
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Uploading your template",
                Work = (w, e) =>
                {
                    var request = new UpdateRequest()
                    {
                        Target = new Entity() {
                            Id = templateId,
                            LogicalName = "documenttemplate",
                            EntityState = EntityState.Changed
                        }
                    };
                    request.Target.Attributes["content"] = Convert.ToBase64String(contentBytes);

                    var response = this.Service.Execute(request);
                },
                ProgressChanged = e =>
                {
                    // If progress has to be notified to user, use the following method:
                    // SetWorkingMessage("Message to display");
                },
                PostWorkCallBack = e =>
                {
                    // SetWorkingMessage("Your Document Template has been updated.");
                    LoadDocumentTemplates();
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }
        #endregion

        private void EditControl_OnComplete(bool userCanceled, EventArgs e)
        {
            _editControl.Dispose();
            _editControl = null;

            if (!userCanceled)
            {
                ExecuteMethod(LoadDocumentTemplates);
            }
            ToggleMainControlsEnabled(true);
        }

        private void toolStripMenuItemUploadSingle_Click(object sender, EventArgs e)
        {
            if (this.listViewDocumentTemplates.SelectedItems.Count != 1) {
                MessageBox.Show("Please select only one template to update.");
            }
            else {
                PerformUploadSingle();
            }
        }

        private void toolStripMenuItemUploadMultiple_Click(object sender, EventArgs e)
        {
            MessageBox.Show("We will refresh the list of existing templates from the server to ensure we have the most up to date list.  Once complete, you will be prompted to select one or more files to upload.");

            // first load the templates again
            LoadDocumentTemplates(PerformUploadMultiple);

        }

        #region instructions helpers

        private void labelInstructions_MouseHover(object sender, EventArgs e)
        {
            this.toolTipInstructions.Show("Double click the instructions to expand.", labelInstructions);
        }

        private void labelInstructions_DoubleClick(object sender, EventArgs e)
        {
            var height = 100;

            if (labelInstructions.Tag == null)
            {
                height = 225;
                labelInstructions.Tag = "expanded";
            }
            else
            {
                height = 100;
                labelInstructions.Tag = null;
            }
            var rowIndex = tableLayoutPanelMain.GetRow(labelInstructions);
            tableLayoutPanelMain.RowStyles[rowIndex].Height = height;
        }
        #endregion
    }
}