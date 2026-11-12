// Household budget settings: incomes, paydays, funds, limits and debts.
// The page is rendered on the server; this file adds rows, keeps the fund and payday pickers
// in sync, shows live totals and sends the whole profile back as JSON.
function loadBudget(template) {
    $.get(`/Budget/Settings${template ? '?template=true' : ''}`, function (data) {
        if (data) {
            $("#budget-tab-pane").html(data);
            initBudgetSettings();
            loadBudgetAccount();
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
