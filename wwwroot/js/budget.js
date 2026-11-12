// Household budget settings: incomes, paydays, funds, limits and debts.
// The page is rendered on the server; this file adds rows, keeps the fund and payday pickers
// in sync, shows live totals and sends the whole profile back as JSON.
function loadBudget(template) {
    $.get(`/Budget/Settings${template ? '?template=true' : ''}`, function (data) {
        if (data) {
            $("#budget-tab-pane").html(data);
            initBudgetSettings();
            loadBudgetAccount();
            loadBudgetReconciliation();
        }
    });
}
function loadBudgetTemplate() {
    var container = $("#budgetSettings");
    if (container.data('has-profile') === true || container.data('has-profile') === 'true') {
        Swal.fire({
            text: container.data('confirm-template'),
            showCancelButton: true,
            confirmButtonText: container.data('confirm'),
            cancelButtonText: container.data('cancel')
        }).then(function (result) {
            if (result.isConfirmed) {
                loadBudget(true);
            }
        });
    } else {
        loadBudget(true);
    }
}
function initBudgetSettings() {
    refreshBudgetPickers();
    updateBudgetTotals();
    $("#budgetSettings").on('input change', 'input, select', function () {
        if ($(this).is('.fund-name, .fund-kind, .income-name')) {
            refreshBudgetPickers();
        }
        updateBudgetTotals();
    });
    $("#budgetSettings .budget-time").each(function () {
        var time = new Date($(this).attr('datetime'));
        if (!isNaN(time)) {
            $(this).text(time.toLocaleString());
        }
    });
}
function newBudgetKey(prefix) {
    return `${prefix}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}
function addBudgetRow(kind) {
    var template = document.getElementById(`budgetTemplate-${kind}`);
    if (!template) {
        return;
    }
    var row = $(template.content.firstElementChild.cloneNode(true));
    if (row.is('[data-key]')) {
        row.attr('data-key', newBudgetKey(kind));
    }
    var target = {
        fund: '#budgetFundRows', category: '#budgetCategoryRows', income: '#budgetIncomeRows',
        allocation: '#budgetAllocationRows', debt: '#budgetDebtRows'
    }[kind];
    $(target).append(row);
    refreshBudgetPickers();
    updateBudgetTotals();
    row.find('input, select').first().focus();
}
function removeBudgetRow(button) {
    $(button).closest('tr').remove();
    refreshBudgetPickers();
    updateBudgetTotals();
}
// Fill every fund and payday picker from the rows on the page, keeping each picker's choice.
function refreshBudgetPickers() {
    var funds = $("#budgetFundRows .budget-fund").map(function () {
        return { key: $(this).attr('data-key'), name: $(this).find('.fund-name').val() || '---', kind: parseInt($(this).find('.fund-kind').val()) };
    }).get();
    var incomes = $("#budgetIncomeRows .budget-income").map(function () {
        return { key: $(this).attr('data-key'), name: $(this).find('.income-name').val() || '---' };
    }).get();
    $("#budgetSettings select.fund-ref").each(function () {
        var picker = $(this);
        var selected = picker.val() || picker.attr('data-selected') || '';
        var onlyKind = picker.attr('data-kind');
        picker.empty();
        funds.filter(x => onlyKind === undefined || x.kind === parseInt(onlyKind)).forEach(function (fund) {
            picker.append($('<option>').val(fund.key).text(fund.name));
        });
        if (selected && picker.find(`option[value="${CSS.escape(selected)}"]`).length) {
            picker.val(selected);
        }
        picker.attr('data-selected', picker.val() || '');
    });
    $("#budgetSettings select.income-ref").each(function () {
        var picker = $(this);
        var selected = picker.val() || picker.attr('data-selected') || '';
        picker.empty();
        incomes.forEach(function (income) {
            picker.append($('<option>').val(income.key).text(income.name));
        });
        if (selected && picker.find(`option[value="${CSS.escape(selected)}"]`).length) {
            picker.val(selected);
        }
        picker.attr('data-selected', picker.val() || '');
    });
    $("#budgetFundRows .budget-fund").each(function () {
        var isPersonal = parseInt($(this).find('.fund-kind').val()) === 1;
        $(this).find('.fund-owner').prop('disabled', !isPersonal);
    });
}
function budgetNumber(input) {
    var value = parseInt($(input).val(), 10);
    return isNaN(value) ? 0 : value;
}
function formatBudgetMoney(value) {
    var currency = $("#budgetCurrency").val() || 'VND';
    try {
        return value.toLocaleString(undefined, { style: 'currency', currency: currency, maximumFractionDigits: 0 });
    } catch {
        return value.toLocaleString();
    }
}
function updateBudgetTotals() {
    var income = 0, funds = 0;
    $("#budgetIncomeRows .income-amount").each(function () { income += budgetNumber(this); });
    $("#budgetFundRows .fund-amount").each(function () { funds += budgetNumber(this); });
    $("#budgetTotalIncome").text(formatBudgetMoney(income));
    $("#budgetTotalFunds").text(formatBudgetMoney(funds));
    var unallocated = income - funds;
    $("#budgetUnallocated").text(formatBudgetMoney(unallocated)).toggleClass('text-danger', unallocated !== 0);
}
function collectBudget() {
    var categories = {};
    $("#budgetCategoryRows .budget-category").each(function () {
        var fundKey = $(this).find('.fund-ref').val();
        if (!fundKey) {
            return;
        }
        (categories[fundKey] = categories[fundKey] || []).push({
            key: $(this).attr('data-key') || newBudgetKey('category'),
            name: $(this).find('.category-name').val(),
            monthlyLimit: budgetNumber($(this).find('.category-limit'))
        });
    });
    var allocations = {};
    $("#budgetAllocationRows .budget-allocation").each(function () {
        var incomeKey = $(this).find('.income-ref').val();
        if (!incomeKey) {
            return;
        }
        (allocations[incomeKey] = allocations[incomeKey] || []).push({
            fundKey: $(this).find('.fund-ref').val() || '',
            amount: budgetNumber($(this).find('.allocation-amount')),
            order: budgetNumber($(this).find('.allocation-order'))
        });
    });
    var optionalNumber = function (input, parse) {
        var text = $(input).val();
        if (text === undefined || text === '') {
            return null;
        }
        var value = parse(text);
        return isNaN(value) ? null : value;
    };
    return {
        currency: $("#budgetCurrency").val(),
        approvalThreshold: budgetNumber($("#budgetThreshold")),
        cycleStartDay: budgetNumber($("#budgetCycleStart")),
        sharedAccount: {
            accountNumber: ($("#budgetAccountNumber").val() || '').replace(/\s/g, ''),
            bankName: $("#budgetAccountBank").val() || ''
        },
        funds: $("#budgetFundRows .budget-fund").map(function () {
            var key = $(this).attr('data-key');
            var kind = parseInt($(this).find('.fund-kind').val());
            return {
                key: key,
                name: $(this).find('.fund-name').val(),
                kind: kind,
                monthlyAmount: budgetNumber($(this).find('.fund-amount')),
                targetAmount: budgetNumber($(this).find('.fund-target')),
                ownerUserId: kind === 1 ? budgetNumber($(this).find('.fund-owner')) : 0,
                categories: categories[key] || []
            };
        }).get(),
        incomes: $("#budgetIncomeRows .budget-income").map(function () {
            var key = $(this).attr('data-key');
            return {
                key: key,
                name: $(this).find('.income-name').val(),
                amount: budgetNumber($(this).find('.income-amount')),
                payDay: budgetNumber($(this).find('.income-day')),
                allocations: allocations[key] || []
            };
        }).get(),
        debts: $("#budgetDebtRows .budget-debt").map(function () {
            return {
                key: $(this).attr('data-key'),
                name: $(this).find('.debt-name').val(),
                monthlyPayment: budgetNumber($(this).find('.debt-payment')),
                dueDay: budgetNumber($(this).find('.debt-day')),
                remainingBalance: optionalNumber($(this).find('.debt-balance'), x => parseInt(x, 10)),
                annualInterestRate: optionalNumber($(this).find('.debt-rate'), parseFloat),
                fundKey: $(this).find('.fund-ref').val() || ''
            };
        }).get()
    };
}
function saveBudget() {
    $.ajax({
        url: '/Budget/Save',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(collectBudget()),
        success: function (result) {
            if (result.success) {
                successToast(result.message);
                loadBudget(false);
            } else {
                var list = $('<ul class="mb-0">');
                (result.message || '').split('\n').filter(x => x).forEach(function (line) {
                    list.append($('<li>').text(line));
                });
                $("#budgetMessages").empty().append($('<div class="alert alert-danger mb-0">').append(list));
                $("#budgetMessages")[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        },
        error: function () {
            errorToast(genericErrorMessage());
        }
    });
}
// Times come from the server in UTC; show them in the phone's own time zone.
function showBudgetTimes(container, options) {
    $(container).find(".budget-time").each(function () {
        var time = new Date($(this).attr('datetime'));
        if (!isNaN(time)) {
            $(this).text(time.toLocaleString(undefined, options));
        }
    });
}
// Joint account card (SePay): balance, money in and out this cycle, latest transactions.
function loadBudgetAccount() {
    $.get('/Budget/Account', function (data) {
        $("#budgetAccount").html(data);
        showBudgetTimes("#budgetAccount");
    });
}
// Home dashboard of the joint account (the default screen of Family Finance).
function loadFinanceDashboard() {
    $.get('/Budget/Dashboard', function (data) {
        if (data) {
            $("#overview-tab-pane").html(data);
            //short form on the phone screen: "12/11, 05:01"
            showBudgetTimes("#overview-tab-pane", { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
        }
    });
}
function openBudgetSettings() {
    $("#budget-tab").first().tab('show');
}
function reloadBudgetAccountViews() {
    if ($("#financeDashboard").length) {
        loadFinanceDashboard();
    }
    if ($("#budgetAccount").length) {
        loadBudgetAccount();
    }
    if ($("#budgetReconciliation").length) {
        loadBudgetReconciliation();
    }
}
function syncBudgetAccount(button) {
    $(button).prop('disabled', true);
    $.ajax({
        url: '/Budget/SyncAccount',
        type: 'POST',
        contentType: 'application/json',
        data: '{}',
        success: function (result) {
            if (result.success) {
                successToast(result.message);
            } else {
                errorToast(result.message);
            }
            reloadBudgetAccountViews();
        },
        error: function () {
            errorToast(genericErrorMessage());
            $(button).prop('disabled', false);
        }
    });
}
// Reconciliation of the joint account: differences between the bank balance and the recorded
// transactions, explained by hand or filled in from an imported bank statement.
function loadBudgetReconciliation() {
    $.get('/Budget/Reconciliation', function (data) {
        $("#budgetReconciliation").html(data);
        showBudgetTimes("#budgetReconciliation");
    });
}
function toggleGapForm(key) {
    $(`#budgetReconciliation li[data-gap-key='${key}'] .gap-form`).toggle();
}
function postBudgetJson(url, payload, onDone) {
    $.ajax({
        url: url,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(payload),
        success: function (result) {
            if (result.success) {
                successToast(result.message);
                reloadBudgetAccountViews();
            } else {
                errorToast(result.message);
            }
            if (onDone) { onDone(result); }
        },
        error: function () {
            errorToast(genericErrorMessage());
            if (onDone) { onDone(); }
        }
    });
}
function saveGapExplanation(key) {
    var item = $(`#budgetReconciliation li[data-gap-key='${key}']`);
    postBudgetJson('/Budget/AddManualTransaction', {
        gapKey: key,
        amount: parseInt(item.find('.gap-amount').val()) || 0,
        date: item.find('.gap-date').val(),
        category: item.find('.gap-category').val(),
        note: item.find('.gap-note').val()
    });
}
function deleteManualEntry(id) {
    postBudgetJson('/Budget/DeleteManualTransaction', { id: id });
}
function importStatement(apply, button) {
    var input = $("#statementFile")[0];
    if (!input || !input.files || !input.files.length) {
        $("#statementFile").addClass('is-invalid');
        return;
    }
    $("#statementFile").removeClass('is-invalid');
    var form = new FormData();
    form.append('file', input.files[0]);
    $(button).prop('disabled', true);
    $.ajax({
        url: `/Budget/ImportStatement?apply=${apply ? 'true' : 'false'}`,
        type: 'POST',
        data: form,
        processData: false,
        contentType: false,
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        success: function (result) {
            $(button).prop('disabled', false);
            showStatementResult(result, apply);
            if (apply && result.success) {
                successToast(result.message);
                reloadBudgetAccountViews();
            }
        },
        error: function () {
            $(button).prop('disabled', false);
            errorToast(genericErrorMessage());
        }
    });
}
function showStatementResult(result, applied) {
    var container = $("#statementResult");
    var esc = function (text) { return $('<div>').text(text == null ? '' : text).html(); };
    if (!result.success) {
        $("#statementApply").hide();
        container.html(`<div class="alert alert-danger mb-0">${(result.errors || []).map(esc).join('<br />')}</div>`);
        return;
    }
    var labels = { New: container.data('new'), AlreadyRecorded: container.data('known'), ReplacesManual: container.data('replace') };
    var columns = Object.keys(result.columns || {}).map(k => `${esc(k)} = "${esc(result.columns[k])}"`).join(', ');
    var rows = (result.preview || []).map(function (row) {
        var when = new Date(row.occurredAtUtc).toLocaleString(undefined, { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
        var amount = (row.direction === 'in' ? '+' : '-') + formatBudgetMoney(row.amount);
        var badge = row.outcome === 'New' ? 'text-bg-primary' : row.outcome === 'ReplacesManual' ? 'text-bg-warning' : 'text-bg-secondary';
        return `<tr><td class="text-nowrap small">${esc(when)}</td><td class="small text-break">${esc(row.description)}</td>` +
            `<td class="text-end text-nowrap ${row.direction === 'in' ? 'text-success' : 'text-danger'}">${esc(amount)}</td>` +
            `<td class="text-end text-nowrap small">${row.balance == null ? '' : esc(formatBudgetMoney(row.balance))}</td>` +
            `<td><span class="badge ${badge}">${esc(labels[row.outcome] || row.outcome)}</span></td></tr>`;
    }).join('');
    container.html(`<div class="alert ${applied ? 'alert-success' : 'alert-info'} mb-2">${esc(result.message)}` +
        (columns ? `<div class="small mt-1">${esc(container.data('columns'))}: ${columns}</div>` : '') +
        (result.skippedLines ? `<div class="small">${esc(container.data('skipped'))}: ${result.skippedLines}</div>` : '') + `</div>` +
        (rows ? `<div class="table-responsive" style="max-height:360px"><table class="table table-sm align-middle mb-0"><tbody>${rows}</tbody></table></div>` : ''));
    if (!applied && (result.new > 0 || result.replacesManual > 0)) {
        $("#statementApply").show();
    } else {
        $("#statementApply").hide();
    }
}
