// AI assistant behind the floating button (Views/Shared/_AssistantLauncher.cshtml).
// The panel loads on first open; the server keeps each user's history for 30 days.
// Replies are rendered with .text() only, so nothing from a message or a photo can inject markup.
var assistantLoaded = false;
var assistantLoading = false;
var assistantOldest = null;
var assistantImageHint = '';

function openAssistantPanel() {
    var panel = document.getElementById('assistantPanel');
    if (panel) {
        bootstrap.Offcanvas.getOrCreateInstance(panel).show();
    }
}
$(function () {
    var panel = document.getElementById('assistantPanel');
    if (!panel) {
        return;
    }
    panel.addEventListener('show.bs.offcanvas', function () {
        $('#assistantLauncher').addClass('assistant-open');
        loadAssistantPanel();
    });
    panel.addEventListener('shown.bs.offcanvas', function () {
        $('#assistantInput').trigger('focus');
        scrollAssistantToBottom();
    });
    panel.addEventListener('hidden.bs.offcanvas', function () {
        $('#assistantLauncher').removeClass('assistant-open');
    });
});
function loadAssistantPanel() {
    if (assistantLoaded || assistantLoading) {
        return;
    }
    assistantLoading = true;
    $.get('/Ai/Assistant', function (data) {
        $('#assistantPanelBody').html(data);
        assistantLoaded = true;
        loadAssistantHistory(false);
        $('#assistantInput').trigger('focus');
    }).fail(function () {
        errorToast(genericErrorMessage());
    }).always(function () {
        assistantLoading = false;
    });
}
function loadAssistantHistory(older) {
    var url = '/Ai/History' + (older && assistantOldest ? `?before=${assistantOldest}` : '');
    $.get(url, function (page) {
        var items = page.items || [];
        if (!items.length) {
            $('#assistantOlder').addClass('d-none');
            return;
        }
        $('#assistantWelcome').addClass('d-none');
        var container = $('<div></div>');
        items.forEach(function (item) {
            container.append(buildAssistantStored(item));
        });
        //older pages go above what is shown; the first page sits above messages sent since the panel opened.
        $('#assistantOlder').after(container.children());
        if (!older) {
            scrollAssistantToBottom();
        }
        assistantOldest = page.nextBefore;
        $('#assistantOlder').toggleClass('d-none', !page.nextBefore);
    });
}
function buildAssistantStored(item) {
    var fromUser = item.role == 'user';
    var wrapper = $('<div class="mb-2"></div>');
    wrapper.append(buildAssistantBubble(item.text, fromUser));
    if (!fromUser && item.facts && item.facts.length) {
        wrapper.append(buildAssistantFacts(item.facts));
    }
    var time = new Date(item.createdAtUtc);
    if (!isNaN(time)) {
        wrapper.append($('<div class="small text-body-secondary"></div>').addClass(fromUser ? 'text-end' : '').text(time.toLocaleString([], { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })));
    }
    return wrapper;
}
function buildAssistantBubble(text, fromUser) {
    var bubble = $('<div class="d-flex"></div>').addClass(fromUser ? 'justify-content-end' : 'justify-content-start');
    var body = $('<div class="rounded-3 px-3 py-2 assistant-bubble"></div>').addClass(fromUser ? 'bg-primary text-white' : 'bg-body-secondary');
    body.text(text);
    bubble.append(body);
    return bubble;
}
function buildAssistantFacts(facts) {
    var list = $('<dl class="assistant-facts row g-0 border rounded-3 px-2 pt-2 mt-1 mb-0"></dl>');
    facts.forEach(function (fact) {
        list.append($('<dt class="col-7 text-truncate"></dt>').text(fact.label).attr('title', fact.label));
        list.append($('<dd class="col-5"></dd>').text(fact.value));
    });
    return list;
}
function appendAssistantMessage(text, fromUser) {
    var bubble = buildAssistantBubble(text, fromUser).addClass('mb-2');
    $("#assistantMessages").append(bubble);
    $('#assistantWelcome').addClass('d-none');
    scrollAssistantToBottom();
    return bubble;
}
function appendAssistantPending() {
    var bubble = buildAssistantBubble('', false).addClass('mb-2');
    bubble.find('.assistant-bubble').html('<span class="spinner-grow spinner-grow-sm me-1" aria-hidden="true"></span><span class="spinner-grow spinner-grow-sm me-1" aria-hidden="true"></span><span class="spinner-grow spinner-grow-sm" aria-hidden="true"></span>');
    $("#assistantMessages").append(bubble);
    scrollAssistantToBottom();
    return bubble;
}
function setAssistantSuggestions(list) {
    var container = $('#assistantSuggestions');
    if (!list || !list.length) {
        return;
    }
    container.empty();
    list.forEach(function (text) {
        container.append($('<button type="button" class="btn btn-sm btn-outline-primary rounded-pill assistant-chip"></button>').text(text).on('click', function () { sendAssistantSuggestion(this); }));
    });
}
function sendAssistantSuggestion(sender) {
    $('#assistantInput').val($(sender).text());
    sendAssistantMessage();
}
//shows one reply: text, facts, warnings, then the draft, payment or food card when there is one.
function showAssistantReply(data) {
    if (!data) {
        errorToast(genericErrorMessage());
        return;
    }
    var wrapper = $('<div class="mb-2"></div>');
    if (data.text) {
        wrapper.append(buildAssistantBubble(data.text, false));
    }
    if (data.facts && data.facts.length) {
        wrapper.append(buildAssistantFacts(data.facts));
    }
    if (data.warnings && data.warnings.length && !data.draft) {
        var warnings = $('<div class="small text-warning-emphasis mt-1"></div>');
        data.warnings.forEach(function (warning) {
            warnings.append($('<div></div>').text(warning).prepend('<i class="bi bi-exclamation-triangle me-1"></i>'));
        });
        wrapper.append(warnings);
    }
    $("#assistantMessages").append(wrapper);
    var matchCard = null;
    if (data.payment && data.matches && data.matches.length > 0) {
        matchCard = appendAssistantMatches(data);
    }
    if (data.draft && (data.kind == 'draft' || data.kind == 'payment')) {
        var draftCard = appendAssistantDraft(data);
        if (matchCard) {
            matchCard.data('draftCard', draftCard);
        }
    }
    if (data.kind == 'food' && data.food) {
        var template = document.getElementById('assistantFoodTemplate');
        var actions = $(template.content.cloneNode(true)).children().first();
        actions.data('reading', data.food.reading);
        $("#assistantMessages").append(actions);
    }
    setAssistantSuggestions(data.suggestions);
    scrollAssistantToBottom();
}
function assistantFailed(pending, xhr) {
    pending.remove();
    if (xhr && xhr.responseJSON && xhr.responseJSON.text) {
        appendAssistantMessage(xhr.responseJSON.text, false);
    } else {
        errorToast(genericErrorMessage());
    }
}
function sendAssistantMessage() {
    var input = $("#assistantInput");
    var message = (input.val() || '').trim();
    if (message == '' || $('#assistantSend').prop('disabled')) {
        return;
    }
    appendAssistantMessage(message, true);
    input.val('');
    setAssistantBusy(true);
    var pending = appendAssistantPending();
    $.post('/Ai/Chat', { message: message }, function (data) {
        pending.remove();
        showAssistantReply(data);
    }).fail(function (xhr) {
        assistantFailed(pending, xhr);
    }).always(function () {
        setAssistantBusy(false);
        input.trigger('focus');
    });
}
function clearAssistantHistory() {
    Swal.fire({
        text: $('#assistantClearConfirm').text(),
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'OK'
    }).then(function (result) {
        if (!result.isConfirmed) {
            return;
        }
        $.post('/Ai/ClearHistory', {}, function (data) {
            if (data.success) {
                $('#assistantMessages').children().not('#assistantOlder, #assistantWelcome').remove();
                $('#assistantOlder').addClass('d-none');
                $('#assistantWelcome').removeClass('d-none');
                assistantOldest = null;
            } else {
                errorToast(genericErrorMessage());
            }
        }).fail(function () { errorToast(genericErrorMessage()); });
    });
}
function pickAssistantImage(hint) {
    assistantImageHint = hint || '';
    $('#assistantImageInput').trigger('click');
}
function sendAssistantImage(sender) {
    var file = sender.files[0];
    sender.value = '';
    if (!file) {
        return;
    }
    var hint = assistantImageHint;
    assistantImageHint = '';
    var preview = $('<div class="d-flex mb-2 justify-content-end"></div>');
    var previewUrl = URL.createObjectURL(file);
    preview.append($('<img class="rounded border" style="max-width:45%; max-height:240px;">').attr('src', previewUrl).attr('alt', file.name));
    $("#assistantMessages").append(preview);
    $('#assistantWelcome').addClass('d-none');
    var pending = appendAssistantPending();
    setAssistantBusy(true);
    var fail = function (xhr) {
        setAssistantBusy(false);
        assistantFailed(pending, xhr);
    };
    //labels have small print, so they are kept larger than receipts.
    resizeAssistantImage(file, hint == 'food' ? 2000 : 1600).then(function (blob) {
        var formData = new FormData();
        formData.append('file', blob, 'photo.jpg');
        $.ajax({
            url: '/Files/HandleFileUpload', type: 'POST', data: formData, processData: false, contentType: false,
            success: function (location) {
                $.post('/Ai/Image', { location: location, name: file.name.replace(/\.[^.]+$/, '') + '.jpg', hint: hint }, function (data) {
                    pending.remove();
                    setAssistantBusy(false);
                    showAssistantReply(data);
                }).fail(fail);
            },
            error: fail
        });
    }, fail);
}
//hands a food label reading to the Food labels tab, where the numbers are checked before saving.
function openAssistantFoodCheck(sender) {
    var reading = $(sender).closest('.assistant-food-actions').data('reading');
    if (!reading) {
        return;
    }
    if ($('#food-tab').length && typeof showFoodCheck === 'function') {
        bootstrap.Offcanvas.getOrCreateInstance(document.getElementById('assistantPanel')).hide();
        if (typeof applyAppModule === 'function') {
            applyAppModule('finance');
        }
        $('#food-tab').first().tab('show');
        waitForElement('#foodCheckForm', function () { showFoodCheck(reading); }, '');
        return;
    }
    try {
        sessionStorage.setItem('assistantFoodReading', JSON.stringify(reading));
        window.location.href = '/Home?tab=food';
    } catch (e) {
        errorToast($('#assistantFoodHandoffError').text());
    }
}
function scrollAssistantToBottom() {
    var container = document.getElementById('assistantMessages');
    if (container) {
        container.scrollTop = container.scrollHeight;
    }
}
function updateAssistantDraftFields(sender) {
    var card = $(sender).closest('.assistant-draft');
    var intent = parseInt(card.find('.draft-intent').val());
    //1 service, 2 fuel, 3 expense, 4 odometer
    card.find('.draft-field-mileage').toggle(intent != 3);
    card.find('.draft-field-fuel, .draft-field-full').toggle(intent == 2);
    card.find('.draft-field-cost').toggle(intent != 4);
    card.find('.draft-field-description, .draft-field-tags').toggle(intent == 1 || intent == 3);
}
function appendAssistantDraft(response) {
    var template = document.getElementById('assistantDraftTemplate');
    var card = $(template.content.cloneNode(true)).children().first();
    var draft = response.draft;
    card.data('notes', draft.notes);
    card.find('.draft-intent').val(draft.intent);
    card.find('.draft-vehicle').val(draft.vehicleId);
    card.find('.draft-date').val(draft.date);
    card.find('.draft-mileage').val(draft.mileage > 0 ? draft.mileage : '');
    card.find('.draft-fuel').val(draft.fuelAmount > 0 ? draft.fuelAmount : '');
    card.find('.draft-cost').val(draft.cost > 0 ? draft.cost : '');
    card.find('.draft-description').val(draft.description);
    card.find('.draft-tags').val((draft.tags || []).join(' '));
    card.find('.draft-full').prop('checked', draft.isFillToFull);
    card.data('files', draft.files || []);
    if ((draft.files || []).length > 0) {
        card.find('.draft-field-attach').css('display', 'block');
        card.find('.draft-attach').prop('checked', draft.attachFiles);
    }
    var warnings = card.find('.assistant-warnings');
    (response.warnings || []).forEach(function (warning) {
        warnings.append($('<div></div>').text(warning).prepend('<i class="bi bi-exclamation-triangle me-1"></i>'));
    });
    $("#assistantMessages").append(card);
    updateAssistantDraftFields(card.find('.draft-intent'));
    scrollAssistantToBottom();
    return card;
}
function cancelAssistantDraft(sender) {
    var card = $(sender).closest('.assistant-draft');
    card.find('input, select').prop('disabled', true);
    card.find('.draft-actions').remove();
    card.find('.draft-cancelled').show();
}
function saveAssistantDraft(sender) {
    var card = $(sender).closest('.assistant-draft');
    var vehicleSelect = card.find('.draft-vehicle');
    var vehicleId = parseInt(vehicleSelect.val());
    if (!vehicleId) {
        vehicleSelect.addClass('is-invalid').focus();
        return;
    }
    vehicleSelect.removeClass('is-invalid');
    var draft = {
        intent: parseInt(card.find('.draft-intent').val()),
        vehicleId: vehicleId,
        date: card.find('.draft-date').val(),
        mileage: parseInt(card.find('.draft-mileage').val()) || 0,
        fuelAmount: parseFloat(card.find('.draft-fuel').val()) || 0,
        cost: parseFloat(card.find('.draft-cost').val()) || 0,
        description: card.find('.draft-description').val(),
        tags: card.find('.draft-tags').val().split(/[\s,]+/).filter(x => x != ''),
        isFillToFull: card.find('.draft-full').is(':checked'),
        notes: card.data('notes') || '',
        files: card.data('files') || [],
        attachFiles: card.find('.draft-attach').is(':checked')
    };
    $(sender).prop('disabled', true);
    $.ajax({
        url: '/Ai/Confirm',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(draft),
        success: function (data) {
            if (data.success) {
                card.find('input, select').prop('disabled', true);
                card.find('.draft-actions').remove();
                card.find('.draft-status').show();
            } else {
                $(sender).prop('disabled', false);
                errorToast(data.message);
            }
        },
        error: function () {
            $(sender).prop('disabled', false);
            errorToast(genericErrorMessage());
        }
    });
}

//shrinks a photo in the browser so uploads are small and the home AI reads it faster.
function resizeAssistantImage(file, maxSide) {
    return new Promise(function (resolve, reject) {
        var image = new Image();
        var url = URL.createObjectURL(file);
        image.onload = function () {
            var scale = Math.min(1, maxSide / Math.max(image.width, image.height));
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(image.width * scale);
            canvas.height = Math.round(image.height * scale);
            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
            URL.revokeObjectURL(url);
            canvas.toBlob(function (blob) { blob ? resolve(blob) : reject(); }, 'image/jpeg', 0.88);
        };
        image.onerror = function () { URL.revokeObjectURL(url); reject(); };
        image.src = url;
    });
}
function setAssistantBusy(busy) {
    $("#assistantSend, #assistantImageButton").prop('disabled', busy);
}
function appendAssistantMatches(response) {
    var template = document.getElementById('assistantMatchTemplate');
    var card = $(template.content.cloneNode(true)).children().first();
    var payment = response.payment;
    card.find('.match-summary').text([payment.amountText, payment.date, payment.bank, payment.recipient, payment.content].filter(x => x).join(' · '));
    var list = card.find('.match-list');
    response.matches.forEach(function (match) {
        var row = $('<div class="list-group-item d-flex justify-content-between align-items-center gap-2"></div>');
        row.append($('<span class="small"></span>').text(`${match.vehicleName} - ${match.description} (${match.date}, ${match.costText})`));
        var button = $('<button type="button" class="btn btn-sm btn-primary text-nowrap"></button>').text($('#assistantMarkPaidLabel').text());
        button.on('click', function () { attachAssistantPayment(card, match, payment, response.draft.files || [], button); });
        row.append(button);
        list.append(row);
    });
    $("#assistantMessages").append(card);
    scrollAssistantToBottom();
    return card;
}
function attachAssistantPayment(card, match, payment, files, button) {
    button.prop('disabled', true);
    var draftCard = card.data('draftCard');
    $.ajax({
        url: '/Ai/AttachPayment',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ recordType: match.recordType, recordId: match.recordId, payment: payment, files: files, attachFiles: draftCard ? draftCard.find('.draft-attach').is(':checked') : false }),
        success: function (data) {
            if (data.success) {
                card.find('button').remove();
                card.find('.match-hint').remove();
                card.find('.match-status').show();
                if (draftCard) {
                    draftCard.remove();
                }
            } else {
                button.prop('disabled', false);
                errorToast(data.message);
            }
        },
        error: function () {
            button.prop('disabled', false);
            errorToast(genericErrorMessage());
        }
    });
}
