function loadAssistant() {
    $.get('/Ai/Assistant', function (data) {
        if (data) {
            $("#assistant-tab-pane").html(data);
            $("#assistantInput").focus();
        }
    });
}
function appendAssistantMessage(text, fromUser) {
    var bubble = $('<div class="d-flex mb-2"></div>').addClass(fromUser ? 'justify-content-end' : 'justify-content-start');
    var body = $('<div class="rounded px-3 py-2" style="max-width:85%; white-space:pre-wrap;"></div>').addClass(fromUser ? 'bg-primary text-white' : 'bg-body-secondary');
    body.text(text);
    bubble.append(body);
    $("#assistantMessages").append(bubble);
    scrollAssistantToBottom();
    return bubble;
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
    var warnings = card.find('.assistant-warnings');
    (response.warnings || []).forEach(function (warning) {
        warnings.append($('<div></div>').text(warning).prepend('<i class="bi bi-exclamation-triangle me-1"></i>'));
    });
    $("#assistantMessages").append(card);
    updateAssistantDraftFields(card.find('.draft-intent'));
    scrollAssistantToBottom();
}
function sendAssistantMessage() {
    var input = $("#assistantInput");
    var message = input.val().trim();
    if (message == '') {
        return;
    }
    appendAssistantMessage(message, true);
    input.val('');
    $("#assistantSend").prop('disabled', true);
    var pending = appendAssistantMessage('…', false);
    $.post('/Ai/Chat', { message: message }, function (data) {
        pending.remove();
        if (data.isDraft) {
            appendAssistantDraft(data);
        } else {
            appendAssistantMessage(data.reply, false);
        }
    }).fail(function () {
        pending.remove();
        errorToast(genericErrorMessage());
    }).always(function () {
        $("#assistantSend").prop('disabled', false);
        input.focus();
    });
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
        notes: card.data('notes') || ''
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
